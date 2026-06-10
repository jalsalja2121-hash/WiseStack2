using System;
using System.Diagnostics;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Unity.InferenceEngine;
using Debug = UnityEngine.Debug;

namespace ARLogistics.EditorTools
{
    public static class OnnxSmokeEvaluator
    {
        private const string ModelPath = "Assets/Models/yolov8n_logistics.onnx";
        private static readonly TensorShape ExpectedInputShape = new TensorShape(1, 3, 640, 640);

        [MenuItem("Tools/AR Logistics/Evaluate ONNX Smoke Test")]
        public static void Run()
        {
            try
            {
                var modelAsset = AssetDatabase.LoadAssetAtPath<ModelAsset>(ModelPath);
                if (modelAsset == null)
                    throw new InvalidOperationException($"ModelAsset could not be loaded: {ModelPath}");

                var model = ModelLoader.Load(modelAsset);
                Debug.Log($"[ONNX Evaluation] inputs={model.inputs.Count}, outputs={model.outputs.Count}, layers={model.layers.Count}");

                foreach (var input in model.inputs)
                    Debug.Log($"[ONNX Evaluation] input name={input.name}, shape={input.shape}, type={input.dataType}");

                foreach (var output in model.outputs)
                    Debug.Log($"[ONNX Evaluation] output name={output.name}");

                using var inputTensor = new Tensor<float>(ExpectedInputShape);
                using var worker = new Worker(model, BackendType.CPU);

                var stopwatch = Stopwatch.StartNew();
                worker.Schedule(inputTensor);
                var outputTensor = worker.PeekOutput() as Tensor<float>;
                if (outputTensor == null)
                    throw new InvalidOperationException("The model output is not a float tensor.");

                using var cpuOutput = outputTensor.ReadbackAndClone();
                stopwatch.Stop();

                var values = cpuOutput.DownloadToArray();
                var nonFinite = values.Count(value => float.IsNaN(value) || float.IsInfinity(value));
                var min = values.Min();
                var max = values.Max();
                var numClasses = cpuOutput.shape.rank == 3 ? cpuOutput.shape[1] - 4 : 0;
                var numAnchors = cpuOutput.shape.rank == 3 ? cpuOutput.shape[2] : 0;
                var maxConfidence = 0f;
                var detectionsAbove045 = 0;

                for (var anchor = 0; anchor < numAnchors; anchor++)
                {
                    var bestConfidence = 0f;
                    for (var classIndex = 0; classIndex < numClasses; classIndex++)
                        bestConfidence = Math.Max(bestConfidence, cpuOutput[0, 4 + classIndex, anchor]);

                    maxConfidence = Math.Max(maxConfidence, bestConfidence);
                    if (bestConfidence >= 0.45f)
                        detectionsAbove045++;
                }

                Debug.Log(
                    $"[ONNX Evaluation] smoke inference passed; outputShape={cpuOutput.shape}, " +
                    $"values={values.Length}, min={min:G6}, max={max:G6}, nonFinite={nonFinite}, " +
                    $"classes={numClasses}, maxConfidence={maxConfidence:G6}, " +
                    $"anchorsAbove0.45={detectionsAbove045}, cpuElapsedMs={stopwatch.Elapsed.TotalMilliseconds:F1}");

                if (cpuOutput.shape.rank != 3 || cpuOutput.shape[0] != 1 || cpuOutput.shape[2] != 8400)
                    Debug.LogError($"[ONNX Evaluation] Unexpected YOLO output shape: {cpuOutput.shape}");

                if (nonFinite > 0)
                    Debug.LogError($"[ONNX Evaluation] Output contains {nonFinite} non-finite values.");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                throw;
            }
        }
    }
}
