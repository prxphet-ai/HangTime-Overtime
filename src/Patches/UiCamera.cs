using System;
using System.Collections;
using System.Linq;
using UnityEngine;

namespace HangtimeOvertime.Patches
{
    // The mod's menus are drawn by their own overlay camera, stacked on the game's main camera with post-processing off:
    // the game grades its picture (tone mapping: a 50% grey shows at 73%, blue tops out at 80%), which would wash out the
    // menus' colours. The menus live on an otherwise unused layer that the main camera stops drawing; the overlay camera
    // follows the main camera (shake, zoom) as its child. If URP's camera data can't be reached, nothing changes.
    internal static class UiCamera
    {
        private static int layer = -2;
        private static Type dataType;

        // the layer the menus are on (-1: no overlay camera, everything stays as it was)
        public static int Layer
        {
            get
            {
                if (layer != -2) return layer;
                layer = -1;
                dataType = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("UnityEngine.Rendering.Universal.UniversalAdditionalCameraData")).FirstOrDefault(t => t != null);
                if (dataType == null) { Plugin.Log.LogWarning("UI camera: URP camera data not found; menus stay on the main camera"); return layer; }
                for (int i = 31; i >= 8; i--)
                    if (string.IsNullOrEmpty(LayerMask.LayerToName(i))) { layer = i; break; }
                Plugin.Log.LogInfo(layer >= 0 ? $"UI camera: menus on layer {layer}" : "UI camera: no free layer");
                return layer;
            }
        }

        public static bool Ensure(Camera main)
        {
            if (main == null || Layer < 0) return false;
            var existing = main.transform.Find("Overtime UI Camera");
            if (existing != null) return true;
            try
            {
                var mainData = main.GetComponent(dataType) ?? main.gameObject.AddComponent(dataType);
                var go = new GameObject("Overtime UI Camera");
                go.transform.SetParent(main.transform, false);
                var cam = go.AddComponent<Camera>();
                cam.orthographic = main.orthographic;
                cam.orthographicSize = main.orthographicSize;
                cam.nearClipPlane = main.nearClipPlane;
                cam.farClipPlane = main.farClipPlane;
                cam.cullingMask = 1 << Layer;
                cam.clearFlags = CameraClearFlags.Nothing;
                cam.allowHDR = false;
                var data = go.GetComponent(dataType) ?? go.AddComponent(dataType);
                var renderType = dataType.GetProperty("renderType");
                renderType.SetValue(data, Enum.ToObject(renderType.PropertyType, 1));                       // CameraRenderType.Overlay
                dataType.GetProperty("renderPostProcessing")?.SetValue(data, false);
                var stack = dataType.GetProperty("cameraStack")?.GetValue(mainData) as System.Collections.Generic.List<Camera>;
                if (stack == null) { UnityEngine.Object.Destroy(go); Plugin.Log.LogWarning("UI camera: no camera stack"); layer = -1; return false; }
                stack.Add(cam);
                main.cullingMask &= ~(1 << Layer);
                go.AddComponent<UiCameraFollow>().Main = main;
                Plugin.Log.LogInfo("UI camera: overlay added to " + main.name);
                return true;
            }
            catch (Exception e) { Plugin.Log.LogWarning("UI camera: " + e.Message); layer = -1; return false; }
        }

        // a new menu object: on the menus' layer straight away (when the UI camera is in use)
        public static void Own(GameObject go) { if (Layer >= 0 && go != null && Camera.main != null && Camera.main.transform.Find("Overtime UI Camera") != null) go.layer = Layer; }

        // a menu that isn't a kit panel (the character creator, the title popups): keeps everything under it on the menus'
        // layer and drawn without the game's lighting
        public static void Adopt(GameObject root)
        {
            if (root == null || !Ensure(Camera.main)) return;
            if (root.GetComponent<UiLayerKeeper>() == null) root.AddComponent<UiLayerKeeper>();
            SetLayer(root);
        }

        public static void SetLayer(GameObject go)
        {
            if (Layer < 0 || go == null) return;
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;
        }
    }

    // the overlay camera keeps the main camera's zoom (the game zooms on match point, kills, the title intro)
    internal class UiCameraFollow : MonoBehaviour
    {
        public Camera Main;
        private Camera cam;
        private void Awake() => cam = GetComponent<Camera>();
        private void LateUpdate()
        {
            if (Main == null || cam == null) return;
            cam.orthographicSize = Main.orthographicSize;
            cam.orthographic = Main.orthographic;
        }
    }

    internal class UiLayerKeeper : MonoBehaviour
    {
        private int children = -1;
        private void LateUpdate()
        {
            int n = GetComponentsInChildren<Transform>(true).Length;
            if (n == children) return;
            children = n;
            UiCamera.SetLayer(gameObject);
            foreach (var r in GetComponentsInChildren<SpriteRenderer>(true))
                if (r.sharedMaterial == null || r.sharedMaterial.shader == null || !r.sharedMaterial.shader.name.Contains("Unlit")) UiArt.MakeUnlit(r);
        }
    }
}
