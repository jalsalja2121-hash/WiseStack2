using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ARLogistics.UI
{
    /// <summary>Presentation around existing input objects; preserves their controller and voice bindings.</summary>
    public sealed class HomeSetupView
    {
        private readonly Dictionary<InputField,Text> errors = new();
        private RectTransform header, viewport, content;
        private Transform root;
        private Text provenance;
        public void Build(Transform parent, InputField[] fields, UnityEngine.Events.UnityAction next)
        {
            root = parent;
            var voiceRecord = WiseStackUI.Find<Button>(root,"VoiceRecordBtn");
            var voiceConfirm = WiseStackUI.Find<Button>(root,"VoiceConfirmBtn");
            var voiceStatus = WiseStackUI.Find<Text>(root,"VoiceStatusText");
            foreach (var name in new[] { "Header","SettingsCard","VoiceInputPanel","VersionText" })
                WiseStackUI.Find<RectTransform>(root,name)?.gameObject.SetActive(false);
            var bg = WiseStackUI.Find<Image>(root,"Background"); if (bg != null) bg.color = WiseStackTheme.Background;
            header = WiseStackUI.Header(root,"WISESTACK 2  /  01 작업 설정","창고 환경을 설정하세요");
            var scroll = WiseStackUI.Scroll(root,"HomeSetupViewport",out content);
            viewport = (RectTransform)scroll.transform;
            var intro = WiseStackUI.Card(content,"Workflow");
            WiseStackUI.Text(intro,"설정  →  화물 인식  →  적재·추천",24,true,WiseStackTheme.Blue);
            provenance = WiseStackUI.Text(intro,"현재 설정 · 수정 후 다음 단계에서 저장됩니다",22,false,WiseStackTheme.Muted);
            var warehouse = WiseStackUI.Card(content,"WarehouseFields");
            WiseStackUI.Text(warehouse,"창고 정보",28,true);
            Field(warehouse,fields[0],"창고 면적","㎡");
            Field(warehouse,fields[1],"천장 높이","m");
            var pallet = WiseStackUI.Card(content,"PalletFields");
            WiseStackUI.Text(pallet,"팔레트 정보",28,true);
            var row = WiseStackUI.Row(pallet,"Dimensions");
            var width = WiseStackUI.Rect("Width",row); WiseStackUI.Size(width,0,1); WiseStackUI.Vertical(width,0,8);
            var length = WiseStackUI.Rect("Length",row); WiseStackUI.Size(length,0,1); WiseStackUI.Vertical(length,0,8);
            Field(width,fields[2],"가로","m");
            Field(length,fields[3],"세로","m");
            Field(pallet,fields[4],"최대 하중","kg");
            WiseStackUI.Button(content,"설정 저장 후 화물 측정  →",next);
            var voice = WiseStackUI.Card(content,"VoiceEntry");
            WiseStackUI.Text(voice,"음성으로 빠르게 입력",26,true);
            if (voiceStatus != null)
            {
                voiceStatus.transform.SetParent(voice,false);
                voiceStatus.fontSize = 24; voiceStatus.color = WiseStackTheme.Muted;
                voiceStatus.alignment = TextAnchor.UpperLeft; voiceStatus.resizeTextForBestFit = false;
                voiceStatus.horizontalOverflow = HorizontalWrapMode.Wrap; voiceStatus.verticalOverflow = VerticalWrapMode.Truncate;
                voiceStatus.text = "창고 면적과 팔레트 규격을 말해 주세요.";
            }
            var actions = WiseStackUI.Row(voice,"VoiceActions");
            ReparentButton(voiceRecord,actions,"음성 입력",false);
            ReparentButton(voiceConfirm,actions,"확인·저장",false);
            Layout();
        }
        private static void ReparentButton(Button button,Transform parent,string caption,bool primary)
        {
            if (button == null) return;
            button.transform.SetParent(parent,false);
            WiseStackUI.Size((RectTransform)button.transform,96,1);
            WiseStackUI.StyleButton(button,primary,caption);
        }
        private void Field(Transform parent,InputField input,string caption,string unit)
        {
            if (input == null) return;
            var group = WiseStackUI.Rect(input.name+"Group",parent); WiseStackUI.Vertical(group,0,8);
            WiseStackUI.Text(group,caption,24,true);
            input.transform.SetParent(group,false); input.gameObject.SetActive(true);
            var rect=(RectTransform)input.transform; rect.localScale=Vector3.one; WiseStackUI.Size(rect,96);
            var background=WiseStackUI.Surface(rect,WiseStackTheme.Background);
            input.targetGraphic=background;
            var text=input.textComponent; text.fontSize=30; text.color=WiseStackTheme.Ink;
            text.fontStyle=FontStyle.Normal; text.alignment=TextAnchor.MiddleLeft; text.resizeTextForBestFit=false;
            WiseStackUI.Stretch(text.rectTransform,16); text.rectTransform.offsetMax=new Vector2(-70,-16);
            if (input.placeholder is Text placeholder)
            {
                placeholder.fontSize=26; placeholder.color=WiseStackTheme.Muted; placeholder.fontStyle=FontStyle.Normal;
                placeholder.text="입력"; WiseStackUI.Stretch(placeholder.rectTransform,16);
            }
            var suffix=WiseStackUI.Text(rect,unit,24,false,WiseStackTheme.Muted);
            suffix.alignment=TextAnchor.MiddleRight; WiseStackUI.Stretch(suffix.rectTransform,16);
            var error=WiseStackUI.Text(group,"",22,false,WiseStackTheme.Error); error.name="FieldError";
            error.gameObject.SetActive(false); errors[input]=error;
            input.onValueChanged.AddListener(_ =>
            {
                error.gameObject.SetActive(false);
                background.color=WiseStackTheme.Background;
                provenance.text="수정 중 · 다음 단계에서 입력값을 확인하고 저장합니다";
            });
            var colors = ColorBlock.defaultColorBlock;
            colors.selectedColor = new Color(.9f,.95f,1); colors.pressedColor = new Color(.85f,.92f,1);
            input.colors = colors;
        }
        public void Error(InputField input,string message)
        {
            if (!errors.TryGetValue(input,out var label)) return;
            label.text=message; label.gameObject.SetActive(true);
            input.GetComponent<Image>().color=new Color(1,.94f,.94f);
            Canvas.ForceUpdateCanvases(); LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            // Bring the invalid field into the viewport even when the keyboard reduced the screen.
            var point=viewport.InverseTransformPoint(input.transform.position);
            float delta=viewport.rect.yMin+120-point.y;
            if (delta>0) content.anchoredPosition+=new Vector2(0,delta);
            input.Select();
        }
        public void SetProvenance(bool saved)
        {
            provenance.text=saved ? "저장된 작업 설정 · 필요하면 수정하세요" : "기본 설정 · 현장 조건에 맞게 확인하세요";
        }
        public void Layout()
        {
            WiseStackUI.PageLayout(root,header,viewport);
            WiseStackUI.Navigation(root,"HomeBtn");
        }
    }
}
