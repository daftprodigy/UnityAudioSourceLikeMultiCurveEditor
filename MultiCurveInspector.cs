#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Sirenix.Utilities.Editor;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(TargetObect))]
[CanEditMultipleObjects]
internal class CameraConfigInspector : Editor
{
    private class CamCurveWrapper
    {
        public CamCurveType type;
        public readonly GUIContent legend;
        public readonly int id;
        public readonly Color color;
        public readonly SerializedProperty curveProp;
        public float rangeMin;
        public float rangeMax;
        public CamCurveWrapper(CamCurveType type, string legend, int id, Color color, SerializedProperty curveProp,float rangeMin, float rangeMax)
        {
            this.type = type;
            this.legend = new GUIContent(legend);
            this.id = id;
            this.color = color;
            this.curveProp = curveProp;
            this.rangeMin = rangeMin;
            this.rangeMax = rangeMax;
        }
    }

    private enum CamCurveType
    {
        Lod,
        Scale,
        Fov,
        FarClip
    }

    private static TargetObect copied = null;

    private class Styles
    {
        public GUIStyle labelStyle = "ProfilerBadge";
        public string controlledByCurveLabel = "Controlled by curve";
    }

    internal static class ReflectionHelper
    {
        public static Dictionary<string, Dictionary<string, PropertyInfo>> ClassPropertyInfoMap = new();
        public static Dictionary<string, Dictionary<string, MethodInfo>> ClassMethodInfoMap = new();
        public static Dictionary<string, Type> typeMap = new();
        public static Assembly hotFixAssembly;

        public static void Init()
        {
            ClassPropertyInfoMap.Clear();
            ClassMethodInfoMap.Clear();
        }

        public static object InvokeMethod(object target, string className, string methodName, params object[] param)
        {
            MethodInfo method;
            Dictionary<string, MethodInfo> MethodMap;
            if (ClassMethodInfoMap.ContainsKey(className))
                MethodMap = ClassMethodInfoMap[className];
            else
            {
                MethodMap = new Dictionary<string, MethodInfo>();
                ClassMethodInfoMap.Add(className, MethodMap);
            }

            if (!MethodMap.TryGetValue(methodName, out method))
            {
                var classType = GetClassType(className);
                method = classType.GetMethod(methodName);
                MethodMap.Add(methodName, method);
            }

            if (method != null)
            {
                var obj = method.Invoke(target, param);
                return obj;
            }

            Debug.LogError($"Can't find public method {methodName} in {className}");
            return null;
        }

        public static object InvokePrivateMethod(object target, string className, string methodName, BindingFlags bindingFlags, params object[] param)
        {
            MethodInfo method;
            Dictionary<string, MethodInfo> MethodMap;
            if (ClassMethodInfoMap.ContainsKey(className))
                MethodMap = ClassMethodInfoMap[className];
            else
            {
                MethodMap = new Dictionary<string, MethodInfo>();
                ClassMethodInfoMap.Add(className, MethodMap);
            }

            if (!MethodMap.TryGetValue(methodName, out method))
            {
                var classType = GetClassType(className);
                method = classType.GetMethod(methodName, bindingFlags);
                MethodMap.Add(methodName, method);
            }

            if (method != null)
            {
                var obj = method.Invoke(target, param);
                return obj;
            }

            Debug.LogError($"Can't find Private method {methodName} in {className}");
            return null;
        }

        public static Type GetClassType(string className)
        {
            if (hotFixAssembly == null)
                hotFixAssembly = Assembly.Load("UnityEditor.dll");
            if (!typeMap.TryGetValue(className, out var t))
            {
                t = hotFixAssembly.GetType($"UnityEditor.{className}");
                if (t != null) typeMap[className] = t;
            }

            return t;
        }

        public static T GetField<T>(object target, string className, string fieldName)
        {
            var classType = GetClassType(className);
            var fieldInfo = classType.GetField(fieldName);
            T value;
            value = (T)fieldInfo.GetValue(target);
            return value;
        }

        public static void SetField<T>(object target, string className, string fieldName, T value)
        {
            var classType = GetClassType(className);
            var fieldInfo = classType.GetField(fieldName);
            fieldInfo.SetValue(target, value);
        }

        public static T GetProperty<T>(object target, string className, string propertyName)
        {
            var classType = GetClassType(className);
            var propertyInfo = classType.GetProperty(propertyName);
            var value = (T)propertyInfo.GetValue(target, null);
            return value;
        }

        public static void SetProperty<T>(object target, string className, string propertyName, T value)
        {
            var classType = GetClassType(className);
            var propertyInfo = classType.GetProperty(propertyName);
            propertyInfo.SetValue(target, value, null);
        }
    }

    private static object m_CurveEditorSettings;
    private static Styles ms_Styles;
    private CamCurveWrapper[] m_AudioCurves;

    private object m_CurveEditor;
    internal bool[] m_SelectedCurves = new bool[0];

    private Type _type_CurveWrapper;

    private readonly object[] _param_dragSelection = new object[3];

    // Callback for Curve Editor to get axis labels
    public Vector2 GetAxisScalars()
    {
        TargetObect config = target as TargetObect;
        m_AudioCurves[1].rangeMin = config.DEFAULT_MIN_SCALE;
        m_AudioCurves[1].rangeMax = config.DEFAULT_MAX_SCALE;
        foreach (var audioCurve in m_AudioCurves)
        {
            var curveWrapperFromID = ReflectionHelper.InvokePrivateMethod(m_CurveEditor, "CurveEditor", "GetCurveWrapperFromID", BindingFlags.Instance | BindingFlags.NonPublic,
                audioCurve.id);
            var selected = ReflectionHelper.GetField<int>(curveWrapperFromID, "CurveWrapper", "selected");
            if (curveWrapperFromID != null && selected == 1)
            {
                return new(config.PINCH_MAX_VALUE, audioCurve.rangeMax);
            }
        }

        return new Vector2(1, 1);
    }

    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();

        if (Selection.count > 1) return;
        
        InitStyles();

        serializedObject.Update();
        
        UpdateWrappersAndLegend();

        DrawCurvs();

        serializedObject.ApplyModifiedProperties();
   
    }

    private void OnEnable()
    {
        m_AudioCurves = new CamCurveWrapper[]
        {
            new(CamCurveType.Lod, "Lod", 0, new Color(0.70f, 0.70f, 0.20f, 1.0f), serializedObject.FindProperty("cameraLodCurve"),0,10),
            new(CamCurveType.Scale, "CamScale", 1, new Color(0.90f, 0.30f, 0.20f, 1.0f), serializedObject.FindProperty("cameraScaleCurve"),0,800),
 
        };
        TargetObect t = this.target as TargetObect;
        var t_CurveEditorSettings = ReflectionHelper.GetClassType("CurveEditorSettings");
        m_CurveEditorSettings = Activator.CreateInstance(t_CurveEditorSettings);
        SetProperty(m_CurveEditorSettings, "hRangeMin", 0.0f);
        SetProperty(m_CurveEditorSettings, "vRangeMin", 0.0f);
        SetProperty(m_CurveEditorSettings, "vRangeMax", 1.0f);
        SetProperty(m_CurveEditorSettings, "hRangeMax", 1.0f);
        SetProperty(m_CurveEditorSettings, "vSlider", false);
        SetProperty(m_CurveEditorSettings, "hSlider", false);
        SetField(m_CurveEditorSettings, "undoRedoSelection", true);

        // TickStyle hTS = new TickStyle();
        // hTS.tickColor.color = new Color(0.0f, 0.0f, 0.0f, 0.15f);
        // hTS.distLabel = 30;
        // m_CurveEditorSettings.hTickStyle = hTS;
        // TickStyle vTS = new TickStyle();
        // vTS.tickColor.color = new Color(0.0f, 0.0f, 0.0f, 0.15f);
        // vTS.distLabel = 20;
        // m_CurveEditorSettings.vTickStyle = vTS;

        var t_CurveEditor = ReflectionHelper.GetClassType("CurveEditor");
        var t_CurveWrapper = ReflectionHelper.GetClassType("CurveWrapper");
        var arr = Array.CreateInstance(t_CurveWrapper, 0);
        m_CurveEditor = Activator.CreateInstance(t_CurveEditor, new Rect(0, 0, 1000, 100), arr, false);
        SetProperty(m_CurveEditor, "settings", m_CurveEditorSettings);
        SetProperty(m_CurveEditor, "margin", 25);
        ReflectionHelper.InvokeMethod(m_CurveEditor, "CurveEditor", "SetShownHRangeInsideMargins", 0.0f, 1.0f);
        ReflectionHelper.InvokeMethod(m_CurveEditor, "CurveEditor", "SetShownVRangeInsideMargins", 0.0f, 1.1f);
        SetProperty(m_CurveEditor, "ignoreScrollWheelUntilClicked", true);

        Undo.undoRedoPerformed += UndoRedoPerformed;
    }
    
    private void SetField(object target, string filedName, object v)
    {
        var type = target.GetType();
        var f = type.GetField(filedName);
        f.SetValue(target, v);
    }

    private Type GetFieldType(object target, string filedName)
    {
        var type = target.GetType();
        var f = type.GetField(filedName);
        return f.FieldType;
    }

    private void SetProperty(object target, string filedName, object v)
    {
        var type = target.GetType();
        var f = type.GetProperty(filedName);
        f.SetValue(target, v);
    }

    private void OnDisable()
    {
        ReflectionHelper.InvokeMethod(m_CurveEditor, "CurveEditor", "OnDisable");
        Undo.undoRedoPerformed -= UndoRedoPerformed;
    }

    private Array GetCurveWrapperArray()
    {
        _type_CurveWrapper = ReflectionHelper.GetClassType("CurveWrapper");

        var wrappers = new List<object>();

        foreach (var audioCurve in m_AudioCurves)
        {
            if (audioCurve.curveProp == null)
                continue;

            var includeCurve = false;
            var curve = audioCurve.curveProp.animationCurveValue;

            includeCurve = !audioCurve.curveProp.hasMultipleDifferentValues;

            if (includeCurve)
            {
                if (curve.length == 0)
                    Debug.LogError(audioCurve.legend.text + " curve has no keys!");
                else
                    wrappers.Add(GetCurveWrapper(curve, audioCurve));
            }
        }

        var sArray = Array.CreateInstance(ReflectionHelper.GetClassType("CurveWrapper"), wrappers.Count);
        var origin = wrappers.ToArray();
        Array.Copy(origin, sArray, wrappers.Count);
        return sArray;
    }

    private object GetCurveWrapper(AnimationCurve curve, CamCurveWrapper camCurve)
    {
        var colorMultiplier = !EditorGUIUtility.isProSkin ? 0.9f : 1.0f;
        var colorMult = new Color(colorMultiplier, colorMultiplier, colorMultiplier, 1);

        _type_CurveWrapper = ReflectionHelper.GetClassType("CurveWrapper");
        
        var wrapper = Activator.CreateInstance(_type_CurveWrapper);
        SetField(wrapper, "id", camCurve.id);
        SetField(wrapper, "groupId", -1);
        SetField(wrapper, "color", camCurve.color * colorMult);
        SetField(wrapper, "hidden", false);
        SetField(wrapper, "readOnly", false);
        var t_NormalCurveRenderer = ReflectionHelper.GetClassType("NormalCurveRenderer");
        var renderer = Activator.CreateInstance(t_NormalCurveRenderer, curve);
        ReflectionHelper.InvokeMethod(renderer, "NormalCurveRenderer", "SetCustomRange", 0.0f, 1.0f);
        SetProperty(wrapper, "renderer", renderer);
        var myMethod = GetType().GetMethod("GetAxisScalars");
        // var del = Delegate.CreateDelegate(ReflectionHelper.GetClassType("CurveWrapper.GetAxisScalarsCallback"), this, myMethod);
        try
        {
            var fieldType = GetFieldType(wrapper, "getAxisUiScalarsCallback");
            var del = Delegate.CreateDelegate(fieldType, this, myMethod);
            SetField(wrapper, "getAxisUiScalarsCallback", del);
        }
        catch (Exception e)
        {
            Debug.LogError(e);
        }

        return wrapper;
    }

    private static void InitStyles()
    {
        if (ms_Styles == null)
            ms_Styles = new Styles();
    }

    private void UndoRedoPerformed()
    {
        Repaint();
    }

    private void DrawCurvs()
    {
    
        var r = GUILayoutUtility.GetAspectRect(1.333f, GUI.skin.textField);
        if (Event.current.type != EventType.Layout && Event.current.type != EventType.Used) SetProperty(m_CurveEditor, "rect", new Rect(r.x, r.y, r.width, r.height));
        // Draw Curve Editor
        UpdateWrappersAndLegend();

        var rect = ReflectionHelper.GetProperty<Rect>(m_CurveEditor, "CurveEditor", "drawRect");
        GUI.Label(rect, GUIContent.none, "TextField");

        SetProperty(m_CurveEditor, "hRangeLocked", Event.current.shift);
        SetProperty(m_CurveEditor, "vRangeLocked", EditorGUI.actionKey);

        ReflectionHelper.InvokeMethod(m_CurveEditor, "CurveEditor", "OnGUI");

        // Draw legend
        DrawLegend();

        var inLiveEdit = (bool)ReflectionHelper.InvokeMethod(m_CurveEditor, "CurveEditor", "InLiveEdit");

        if (!inLiveEdit)
        {
            // Check if any of the curves changed
            foreach (var audioCurve in m_AudioCurves)
            {
                var curveWrapperFromID = ReflectionHelper.InvokePrivateMethod(m_CurveEditor, "CurveEditor", "GetCurveWrapperFromID", BindingFlags.Instance | BindingFlags.NonPublic,
                    audioCurve.id);
                var changed = ReflectionHelper.GetProperty<bool>(curveWrapperFromID, "CurveWrapper", "changed");
                if (curveWrapperFromID != null && changed)
                {
                    var curve = ReflectionHelper.GetProperty<AnimationCurve>(curveWrapperFromID, "CurveWrapper", "curve");
                    var changedCurve = curve;

                    // Never save a curve with no keys
                    if (changedCurve.length > 0)
                    {
                        audioCurve.curveProp.animationCurveValue = changedCurve;
                        ReflectionHelper.SetProperty(curveWrapperFromID, "CurveWrapper", "changed", false);
                    }
                }
            }
        }
    }

    private void UpdateWrappersAndLegend()
    {
        var inLiveEdit = (bool)ReflectionHelper.InvokeMethod(m_CurveEditor, "CurveEditor", "InLiveEdit");

        if (inLiveEdit)
            return;

        var curveWrapperArray = GetCurveWrapperArray();

        SetProperty(m_CurveEditor, "animationCurves", curveWrapperArray);
        SyncShownCurvesToLegend(GetShownAudioCurves());
    }

    private void DrawLegend()
    {
        var legendRects = new List<Rect>();
        var curves = GetShownAudioCurves();

        var legendRect = GUILayoutUtility.GetRect(10, 20);
        // legendRect.x += 4 + EditorGUI.indent;
        // legendRect.width -= 8 + EditorGUI.indent;
        var width = Mathf.Min(75, Mathf.FloorToInt(legendRect.width / curves.Count));
        for (var i = 0; i < curves.Count; i++) legendRects.Add(new Rect(legendRect.x + width * i, legendRect.y, width, legendRect.height));

        var resetSelections = false;
        if (curves.Count != m_SelectedCurves.Length)
        {
            m_SelectedCurves = new bool[curves.Count];
            resetSelections = true;
        }

        _param_dragSelection[0] = legendRects.ToArray();
        _param_dragSelection[1] = m_SelectedCurves;
        _param_dragSelection[2] = GUIStyle.none;
        var f1 = (bool)ReflectionHelper.InvokeMethod(null, "EditorGUIExt", "DragSelection", _param_dragSelection);
        if (f1 || resetSelections)
        {
            // If none are selected, select all
            var someSelected = false;
            for (var i = 0; i < curves.Count; i++)
            {
                if (m_SelectedCurves[i])
                    someSelected = true;
            }

            if (!someSelected)
                for (var i = 0; i < curves.Count; i++)
                    m_SelectedCurves[i] = true;

            SyncShownCurvesToLegend(curves);
        }

        for (var i = 0; i < curves.Count; i++)
        {
            DrawLegend(legendRects[i], curves[i].color, curves[i].legend.text, m_SelectedCurves[i]);
            if (curves[i].curveProp.hasMultipleDifferentValues) GUI.Button(new Rect(legendRects[i].x, legendRects[i].y + 20, legendRects[i].width, 20), "Different");
        }
    }

    internal static void DrawLegend(Rect position, Color color, string label, bool enabled)
    {
        position = new Rect(position.x + 2f, position.y + 2f, position.width - 2f, position.height - 2f);
        var backgroundColor = GUI.backgroundColor;
        GUI.backgroundColor = enabled ? color : new Color(0.5f, 0.5f, 0.5f, 0.45f);
        GUI.Label(position, label, "ProfilerPaneSubLabel");
        GUI.backgroundColor = backgroundColor;
    }

    private List<CamCurveWrapper> GetShownAudioCurves()
    {
        return m_AudioCurves.Where(f =>
            ReflectionHelper.InvokePrivateMethod(m_CurveEditor, "CurveEditor", "GetCurveWrapperFromID", BindingFlags.Instance | BindingFlags.NonPublic, f.id) != null).ToList();
    }

    private void SyncShownCurvesToLegend(List<CamCurveWrapper> curves)
    {
        if (curves.Count != m_SelectedCurves.Length)
            return; // Selected curves in sync'ed later in this frame

        for (var i = 0; i < curves.Count; i++)
        {
            var curWrapper = ReflectionHelper.InvokePrivateMethod(m_CurveEditor, "CurveEditor", "GetCurveWrapperFromID", BindingFlags.Instance | BindingFlags.NonPublic,
                curves[i].id);
            ReflectionHelper.SetField(curWrapper, "CurveWrapper", "hidden", !m_SelectedCurves[i]);
        }


        // Need to apply animation curves again to synch selections
        var getAC = ReflectionHelper.GetProperty<Array>(m_CurveEditor, "CurveEditor", "animationCurves");
        SetProperty(m_CurveEditor, "animationCurves", getAC);
    }
}
#endif