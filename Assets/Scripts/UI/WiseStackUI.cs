using UnityEngine;
using UnityEngine.UI;

namespace ARLogistics.UI
{
    /// <summary>Shared uGUI building blocks. Units target the existing 720-wide canvases.</summary>
    public static class WiseStackUI
    {
        private static Sprite rounded;
        public static RectTransform Rect(string name, Transform parent)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }
        public static T Find<T>(Transform root, string name) where T : Component
        {
            foreach (var item in root.GetComponentsInChildren<T>(true)) if (item.name == name) return item;
            return null;
        }
        public static Text Text(Transform parent, string value, int size = 26, bool bold = false, Color? color = null)
        {
            var text = Rect("Label", parent).gameObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size; text.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            text.color = color ?? WiseStackTheme.Ink; text.text = value; text.lineSpacing = 1.1f;
            text.supportRichText = false; text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }
        public static void Stretch(RectTransform rect, float padding = 0)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(padding,padding); rect.offsetMax = new Vector2(-padding,-padding);
        }
        public static VerticalLayoutGroup Vertical(RectTransform root, int padding = 24, float gap = 16)
        {
            var group = root.GetComponent<VerticalLayoutGroup>() ?? root.gameObject.AddComponent<VerticalLayoutGroup>();
            group.padding = new RectOffset(padding,padding,padding,padding); group.spacing = gap;
            group.childControlWidth = group.childControlHeight = true;
            group.childForceExpandWidth = true; group.childForceExpandHeight = false;
            return group;
        }
        public static RectTransform Row(Transform parent, string name, float gap = 16)
        {
            var row = Rect(name,parent);
            var group = row.gameObject.AddComponent<HorizontalLayoutGroup>(); group.spacing = gap;
            group.childControlWidth = group.childControlHeight = true;
            group.childForceExpandWidth = true; group.childForceExpandHeight = false;
            group.childAlignment = TextAnchor.MiddleLeft;
            return row;
        }
        public static LayoutElement Size(RectTransform rect, float minHeight = -1, float flexibleWidth = -1)
        {
            var size = rect.GetComponent<LayoutElement>() ?? rect.gameObject.AddComponent<LayoutElement>();
            if (minHeight >= 0) size.minHeight = minHeight;
            if (flexibleWidth >= 0) { size.minWidth = size.preferredWidth = 0; size.flexibleWidth = flexibleWidth; }
            return size;
        }
        public static Image Surface(RectTransform rect, Color color)
        {
            var image = rect.GetComponent<Image>() ?? rect.gameObject.AddComponent<Image>();
            if (rounded == null)
            {
                const int length = 64; const float radius = 16;
                var texture = new Texture2D(length,length,TextureFormat.RGBA32,false);
                texture.hideFlags = HideFlags.HideAndDontSave;
                texture.wrapMode = TextureWrapMode.Clamp;
                var pixels = new Color[length*length];
                for (int y=0; y<length; y++) for (int x=0; x<length; x++)
                {
                    float dx = Mathf.Max(radius-x-.5f, x+.5f-(length-radius), 0);
                    float dy = Mathf.Max(radius-y-.5f, y+.5f-(length-radius), 0);
                    pixels[y*length+x] = new Color(1,1,1,Mathf.Clamp01(radius+.5f-Mathf.Sqrt(dx*dx+dy*dy)));
                }
                texture.SetPixels(pixels); texture.Apply();
                rounded = Sprite.Create(texture,new Rect(0,0,length,length),new Vector2(.5f,.5f),100,0,
                    SpriteMeshType.FullRect,new Vector4(radius,radius,radius,radius));
                rounded.hideFlags = HideFlags.HideAndDontSave;
            }
            image.sprite = rounded; image.type = Image.Type.Sliced; image.color = color;
            foreach (var outline in rect.GetComponents<Outline>()) outline.enabled = false;
            return image;
        }
        public static RectTransform Card(Transform parent, string name)
        {
            var card = Rect(name,parent); Surface(card,WiseStackTheme.Card); Vertical(card); return card;
        }
        public static void StyleButton(Button button, bool primary = true, string caption = null)
        {
            var image = Surface((RectTransform)button.transform, primary ? WiseStackTheme.Blue : WiseStackTheme.Pale);
            button.targetGraphic = image; button.transition = Selectable.Transition.ColorTint;
            var colors = ColorBlock.defaultColorBlock;
            colors.highlightedColor = new Color(.90f,.95f,1);
            colors.pressedColor = new Color(.72f,.84f,.98f);
            colors.disabledColor = new Color(.65f,.69f,.75f,.65f);
            colors.fadeDuration = .12f; button.colors = colors;
            var label = button.GetComponentInChildren<Text>(true);
            if (label == null) label = Text(button.transform,caption ?? "",28,true);
            if (caption != null) label.text = caption;
            label.fontSize = 28; label.fontStyle = FontStyle.Bold;
            label.color = primary ? Color.white : WiseStackTheme.Blue;
            label.resizeTextForBestFit = false; label.raycastTarget = false;
            label.alignment = TextAnchor.MiddleCenter; Stretch(label.rectTransform,12);
        }
        public static Button Button(Transform parent, string caption, UnityEngine.Events.UnityAction action, bool primary = true)
        {
            var rect = Rect("Action",parent); Size(rect,96);
            var button = rect.gameObject.AddComponent<Button>(); StyleButton(button,primary,caption);
            if (action != null) button.onClick.AddListener(action);
            return button;
        }
        public static RectTransform Header(Transform root, string eyebrow, string title)
        {
            var header = Rect("ProductHeader",root); Vertical(header,0,4);
            Text(header,eyebrow,20,true,WiseStackTheme.Muted);
            Text(header,title,34,true);
            return header;
        }
        public static ScrollRect Scroll(Transform parent, string name, out RectTransform content)
        {
            var viewport = Rect(name,parent);
            viewport.gameObject.AddComponent<Image>().color = new Color(1,1,1,.001f);
            viewport.gameObject.AddComponent<RectMask2D>();
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport; scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 36;
            content = Rect("Content",viewport); content.anchorMin = new Vector2(0,1);
            content.anchorMax = Vector2.one; content.pivot = new Vector2(.5f,1); content.sizeDelta = Vector2.zero;
            Vertical(content,0,16);
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = content; return scroll;
        }
        public static void PageLayout(Transform root, RectTransform header, RectTransform viewport)
        {
            var safe = Insets(root);
            header.anchorMin = new Vector2(0,1); header.anchorMax = Vector2.one;
            header.offsetMin = new Vector2(safe.x+24,-safe.w-116);
            header.offsetMax = new Vector2(-safe.z-24,-safe.w-24);
            viewport.anchorMin = Vector2.zero; viewport.anchorMax = Vector2.one;
            viewport.offsetMin = new Vector2(safe.x+24,safe.y+WiseStackTheme.NavigationHeight+16);
            viewport.offsetMax = new Vector2(-safe.z-24,-safe.w-132);
        }
        // left, bottom, right, top, in Canvas units
        public static Vector4 Insets(Transform root)
        {
            var canvas = root.GetComponentInParent<Canvas>();
            float scale = canvas != null ? Mathf.Max(.01f,canvas.scaleFactor) : 1;
            return new Vector4(Screen.safeArea.xMin,Screen.safeArea.yMin,
                Screen.width-Screen.safeArea.xMax,Screen.height-Screen.safeArea.yMax)/scale;
        }
        public static void Navigation(Transform root, string selected)
        {
            var nav = Find<RectTransform>(root,"NavBar"); if (nav == null) return;
            var safe = Insets(root);
            var image = nav.GetComponent<Image>(); if (image != null) image.color = WiseStackTheme.Card;
            nav.anchorMin = Vector2.zero; nav.anchorMax = new Vector2(1,0); nav.pivot = new Vector2(.5f,0);
            nav.offsetMin = new Vector2(safe.x,safe.y);
            nav.offsetMax = new Vector2(-safe.z,safe.y+WiseStackTheme.NavigationHeight);
            string[] names = { "HomeBtn","MeasurementBtn","StackPreviewBtn","DangerOverlayBtn" };
            string[] captions = { "홈","측정","적재","추천" };
            for (int i=0; i<4; i++)
            {
                var button = Find<Button>(nav,names[i]); if (button == null) continue;
                var rect = (RectTransform)button.transform;
                rect.anchorMin = new Vector2(i/4f,0); rect.anchorMax = new Vector2((i+1)/4f,1);
                rect.offsetMin = new Vector2(4,4); rect.offsetMax = new Vector2(-4,-4);
                bool active = names[i] == selected;
                var surface = Surface(rect,active ? WiseStackTheme.Pale : WiseStackTheme.Card);
                button.targetGraphic = surface;
                var colors = ColorBlock.defaultColorBlock; colors.pressedColor = new Color(.82f,.90f,1); button.colors = colors;
                var label = button.GetComponentInChildren<Text>(true);
                if (label != null)
                {
                    label.text = captions[i]; label.fontSize = 20; label.fontStyle = active ? FontStyle.Bold : FontStyle.Normal;
                    label.color = active ? WiseStackTheme.Blue : WiseStackTheme.Muted;
                    label.alignment = TextAnchor.MiddleCenter;
                    label.rectTransform.anchorMin = new Vector2(0,0); label.rectTransform.anchorMax = new Vector2(1,.4f);
                    label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
                }
                var icon = Find<RectTransform>(rect,"TabIcon");
                if (icon == null) { icon = Rect("TabIcon",rect); BuildIcon(icon,i); }
                icon.anchorMin = icon.anchorMax = new Vector2(.5f,.69f);
                icon.sizeDelta = new Vector2(36,36); icon.anchoredPosition = Vector2.zero;
                foreach (var stroke in icon.GetComponentsInChildren<Image>()) stroke.color=active ? WiseStackTheme.Blue : WiseStackTheme.Muted;
            }
            nav.SetAsLastSibling();
        }

        private static void BuildIcon(RectTransform root,int kind)
        {
            void Line(float x,float y,float xx,float yy)
            {
                var a=new Vector2(x-.5f,y-.5f)*36; var b=new Vector2(xx-.5f,yy-.5f)*36;
                var stroke=Rect("Stroke",root); stroke.anchorMin=stroke.anchorMax=new Vector2(.5f,.5f);
                stroke.anchoredPosition=(a+b)/2; stroke.sizeDelta=new Vector2((b-a).magnitude,3);
                stroke.localRotation=Quaternion.Euler(0,0,Mathf.Atan2(b.y-a.y,b.x-a.x)*Mathf.Rad2Deg);
                stroke.gameObject.AddComponent<Image>().raycastTarget=false;
            }
            void Box(float x,float y,float xx,float yy) { Line(x,y,xx,y); Line(xx,y,xx,yy); Line(xx,yy,x,yy); Line(x,yy,x,y); }
            if (kind==0) { Line(.12f,.5f,.5f,.86f); Line(.5f,.86f,.88f,.5f); Line(.22f,.55f,.22f,.14f); Line(.22f,.14f,.78f,.14f); Line(.78f,.14f,.78f,.55f); Line(.45f,.14f,.45f,.42f); }
            else if (kind==1) { Box(.12f,.22f,.88f,.76f); Box(.36f,.34f,.64f,.63f); Line(.28f,.76f,.36f,.9f); Line(.36f,.9f,.65f,.9f); }
            else if (kind==2) { Box(.13f,.12f,.46f,.44f); Box(.54f,.12f,.87f,.44f); Box(.33f,.54f,.67f,.86f); }
            else { Box(.16f,.12f,.84f,.88f); Line(.3f,.35f,.43f,.23f); Line(.43f,.23f,.71f,.56f); Line(.3f,.72f,.65f,.72f); }
        }
    }

}
