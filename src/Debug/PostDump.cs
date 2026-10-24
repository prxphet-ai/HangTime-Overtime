using System.Linq;
using UnityEngine;

namespace HangtimeOvertime.Debugging
{
    // development (DumpScenes): the scene's post-processing volumes and their overridden settings, and the main camera's
    // URP data, so the menus' colours can be planned against the game's grading
    internal static class PostDump
    {
        public static void Log()
        {
            foreach (var v in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(m => m.GetType().Name == "Volume"))
            {
                var t = v.GetType();
                var profile = t.GetProperty("sharedProfile")?.GetValue(v) ?? t.GetField("sharedProfile")?.GetValue(v);
                Plugin.Log.LogInfo($"[post] volume {v.name} global={t.GetField("isGlobal")?.GetValue(v)} weight={t.GetField("weight")?.GetValue(v)} profile={profile}");
                var comps = profile?.GetType().GetField("components")?.GetValue(profile) as System.Collections.IEnumerable;
                if (comps == null) continue;
                foreach (var c in comps)
                {
                    var ct = c.GetType();
                    var active = ct.GetField("active")?.GetValue(c);
                    var pars = ct.GetFields().Where(f => f.FieldType.Name.Contains("Parameter")).Select(f =>
                    {
                        var p = f.GetValue(c);
                        var over = p?.GetType().GetProperty("overrideState")?.GetValue(p);
                        var val = p?.GetType().GetProperty("value")?.GetValue(p);
                        return over is bool b && b ? $"{f.Name}={val}" : null;
                    }).Where(s => s != null);
                    Plugin.Log.LogInfo($"[post]   {ct.Name} active={active}: {string.Join(", ", pars)}");
                }
            }
            var cam = Camera.main;
            if (cam == null) return;
            var data = cam.GetComponents<MonoBehaviour>().FirstOrDefault(m => m.GetType().Name == "UniversalAdditionalCameraData");
            if (data != null)
            {
                var dt = data.GetType();
                Plugin.Log.LogInfo($"[post] camera {cam.name}: renderPostProcessing={dt.GetProperty("renderPostProcessing")?.GetValue(data)} renderType={dt.GetProperty("renderType")?.GetValue(data)} volumeLayerMask={dt.GetProperty("volumeLayerMask")?.GetValue(data)} cullingMask={cam.cullingMask} hdr={cam.allowHDR}");
            }
            Plugin.Log.LogInfo($"[post] colorSpace={QualitySettings.activeColorSpace}");
        }
    }
}
