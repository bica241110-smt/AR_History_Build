using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class DebugTextManager : MonoBehaviour
{
    [Serializable]
    public class BufferEntry
    {
        public string name = "Default";
        [TextArea(2, 8)] public string initialText = "";
        public bool showTimestamp = true;
        public bool appendMode = true;
    }

    // Inspector ???
    public TextMeshProUGUI targetText;
    public Button cycleButton;
    public List<BufferEntry> buffers = new List<BufferEntry>()
    {
        new BufferEntry() { name = "Default", initialText = "", showTimestamp = true }
    };
    public string headerFormat = "[{0}]"; // {0} = buffer name

    // ?P???? singleton (Scene ???? 1 ??)
    public static DebugTextManager Instance { get; private set; }

    // ???s??????
    private readonly Dictionary<string, StringBuilder> _bufferMap = new Dictionary<string, StringBuilder>(StringComparer.Ordinal);
    private readonly List<string> _bufferOrder = new List<string>();
    private int _currentIndex = 0;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // ??????
        _bufferMap.Clear();
        _bufferOrder.Clear();
        foreach (var e in buffers)
        {
            if (string.IsNullOrEmpty(e.name)) continue;
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(e.initialText))
            {
                sb.Append(e.initialText);
                if (e.appendMode) sb.AppendLine();
            }
            _bufferMap[e.name] = sb;
            _bufferOrder.Add(e.name);
        }

        if (_bufferOrder.Count == 0)
        {
            _bufferOrder.Add("Default");
            _bufferMap["Default"] = new StringBuilder();
        }

        if (cycleButton != null)
        {
            cycleButton.onClick.AddListener(CycleNext);
        }

        UpdateDisplay();
    }

    private void OnDestroy()
    {
        if (cycleButton != null)
        {
            cycleButton.onClick.RemoveListener(CycleNext);
        }
        if (Instance == this) Instance = null;
    }

    // public API -------------------------------------------------

    // ???O?????i???????e???L?j
    public static void Log(string bufferName, string message)
    {
        if (Instance == null) return;
        Instance.AppendToBuffer(bufferName ?? "Default", message, true);
    }

    // ?w??o?b?t?@??????
    public static void Set(string bufferName, string message)
    {
        if (Instance == null) return;
        Instance.SetBufferText(bufferName ?? "Default", message);
    }

    // ?o?b?t?@???N???A
    public static void Clear(string bufferName)
    {
        if (Instance == null) return;
        Instance.ClearBuffer(bufferName ?? "Default");
    }

    // ?T?C?N???i?{?^????????j
    public void CycleNext()
    {
        if (_bufferOrder.Count == 0) return;
        _currentIndex = (_currentIndex + 1) % _bufferOrder.Count;
        UpdateDisplay();
    }

    // ?v???O????????w??o?b?t?@??\??
    public void ShowBuffer(string bufferName)
    {
        int idx = _bufferOrder.IndexOf(bufferName);
        if (idx >= 0)
        {
            _currentIndex = idx;
            UpdateDisplay();
        }
    }

    // ???????? ---------------------------------------------------

    private void AppendToBuffer(string bufferName, string message, bool includeTimestamp)
    {
        if (!_bufferMap.ContainsKey(bufferName))
        {
            _bufferMap[bufferName] = new StringBuilder();
            _bufferOrder.Add(bufferName);
        }

        var entryDef = buffers.Find(b => b.name == bufferName);
        bool ts = entryDef?.showTimestamp ?? true;
        string time = includeTimestamp && ts ? $"[{DateTime.Now:HH:mm:ss}] " : "";
        _bufferMap[bufferName].AppendLine(time + message);

        // ?\??????o?b?t?@??????????X?V
        if (_bufferOrder.Count > 0 && _bufferOrder[_currentIndex] == bufferName)
            UpdateDisplay();
    }

    private void SetBufferText(string bufferName, string message)
    {
        if (!_bufferMap.ContainsKey(bufferName))
        {
            _bufferMap[bufferName] = new StringBuilder();
            _bufferOrder.Add(bufferName);
        }

        _bufferMap[bufferName].Clear();
        _bufferMap[bufferName].Append(message);

        if (_bufferOrder[_currentIndex] == bufferName)
            UpdateDisplay();
    }

    private void ClearBuffer(string bufferName)
    {
        if (_bufferMap.ContainsKey(bufferName))
        {
            _bufferMap[bufferName].Clear();
            if (_bufferOrder[_currentIndex] == bufferName) UpdateDisplay();
        }
    }

    private void UpdateDisplay()
    {
        if (targetText == null) return;
        if (_bufferOrder.Count == 0)
        {
            targetText.text = "";
            return;
        }

        string name = _bufferOrder[_currentIndex];
        _bufferMap.TryGetValue(name, out var sb);
        string body = sb?.ToString() ?? "";
        targetText.text = string.Format(headerFormat, name) + "\n" + body;
    }
}