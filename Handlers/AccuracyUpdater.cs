using BepInEx.Unity.IL2CPP.Utils;
using Hikaria.AccuracyTracker.Extensions;
using Player;
using SNetwork;
using System.Collections;
using TheArchive.Utilities;
using TMPro;
using UnityEngine;
using static Hikaria.AccuracyTracker.Features.AccuracyTracker;
using static Hikaria.AccuracyTracker.Managers.AccuracyManager;

namespace Hikaria.AccuracyTracker.Handlers;

public class AccuracyUpdater : MonoBehaviour
{
    private void Awake()
    {
        Instance = this;
        Setup();
        this.StartCoroutine(UpdateAccuracyDataCoroutine());
    }

    private void Setup()
    {
        if (IsSetup)
        {
            return;
        }
        AccuracyTextMeshesVisible[0] = false;
        AccuracyTextMeshesVisible[1] = false;
        AccuracyTextMeshesVisible[2] = false;
        AccuracyTextMeshesVisible[3] = false;

        PUI_Inventory inventory = GuiManager.Current.m_playerLayer.Inventory;
        foreach (RectTransform rectTransform in inventory.m_iconDisplay.GetComponentsInChildren<RectTransform>(true))
        {
            if (rectTransform.name == "Background Fade")
            {
                TextMeshPro textMeshPro = inventory.m_inventorySlots[InventorySlot.GearMelee].m_slim_archetypeName;
                for (int i = 0; i < 4; i++)
                {
                    GameObject gameObject = Instantiate(rectTransform.gameObject, rectTransform.parent);
                    RectTransform component = gameObject.GetComponent<RectTransform>();
                    gameObject.gameObject.SetActive(true);
                    foreach (Transform transform in gameObject.GetComponentsInChildren<Transform>(true))
                    {
                        if (transform.name == "TimerShowObject")
                        {
                            transform.gameObject.active = false;
                        }
                    }
                    gameObject.transform.localPosition = new Vector3(-70f + OffsetX, -62 + OffsetY + -35 * i, 0f);
                    AccuracyTextMeshes[i] = Instantiate(textMeshPro);
                    GameObject gameObject2 = new GameObject($"AccuracyTracker{i}")
                    {
                        layer = 5,
                        hideFlags = HideFlags.HideAndDontSave
                    };
                    gameObject2.transform.SetParent(component.transform, false);
                    AccuracyTextMeshes[i].m_width *= 2;
                    AccuracyTextMeshes[i].transform.SetParent(gameObject2.transform, false);
                    AccuracyTextMeshes[i].GetComponent<RectTransform>().anchoredPosition = new(-5f, 9f);
                    AccuracyTextMeshes[i].SetText("-: -%/-%(0/0)", true);
                    AccuracyTextMeshes[i].ForceMeshUpdate();
                }
                break;
            }
        }
        MarkAllAccuracyDataNeedUpdate();
        IsSetup = true;
    }

    internal static void CheckAndSetVisible()
    {
        if (!SNet.IsMaster)
        {
            foreach (var lookup in AccuracyDataLookup.Keys.ToList())
            {
                if (!IsAccuracyListener(lookup) && AccuracyRegisteredCharacterIndex.TryGetValue(lookup, out var index))
                {
                    SetVisible(index, false, false);
                }
            }
        }
        else
        {
            foreach (var pair in AccuracyRegisteredCharacterIndex)
            {
                SetVisible(pair.Value, true, false);
            }
        }
        UpdateVisible();
    }

    internal static void RegisterPlayer(SNet_Player player)
    {
        if (RegisterPlayerCoroutines.ContainsKey(player.Lookup))
        {
            Instance.StopCoroutine(RegisterPlayerCoroutines[player.Lookup]);
        }
        Instance.StartCoroutine(RegisterPlayerCoroutine(player));
    }

    private static IEnumerator RegisterPlayerCoroutine(SNet_Player player)
    {
        var yielder = new WaitForSecondsRealtime(2f);
        int timeout = 120;
        while (timeout-- > 0)
        {
            if (player == null)
            {
                yield break;
            }
            if (player.HasCharacterSlot && player.CharacterIndex != -1)
            {
                AccuracyRegisteredCharacterIndex[player.Lookup] = player.CharacterIndex;
                AccuracyDataLookup[player.Lookup] = new(player);
                AccuracyDataNeedUpdate[player.Lookup] = true;
                yield break;
            }
            yield return yielder;
        }
    }

    private IEnumerator UpdateAccuracyDataCoroutine()
    {
        var yielder = new WaitForSecondsRealtime(3f);
        while (true)
        {
            foreach (var data in AccuracyDataLookup.Values.ToList())
            {
                var owner = data.Owner;
                if (AccuracyDataNeedUpdate[owner.Lookup] && AccuracyRegisteredCharacterIndex.TryGetValue(owner.Lookup, out var index))
                {
                    UpdateAccuracyData(index, data.GetAccuracyText());
                    AccuracyDataNeedUpdate[owner.Lookup] = false;
                    if (SNet.IsMaster && (owner.IsBot || !IsAccuracyListener(owner.Lookup)) || owner.IsLocal)
                    {
                        SendAccuracyData(data);
                    }
                    if (NeedShowAccuracy(owner))
                    {
                        if (!AccuracyTextMeshesVisible[index])
                        {
                            SetVisible(index, true);
                        }
                    }
                    else if (AccuracyTextMeshesVisible[index])
                    {
                        SetVisible(index, false);
                    }
                }
                yield return null;
            }
            yield return yielder;
        }
    }

    internal void UpdateAccuracyData(pAccuracyData data)
    {
        if (!data.Owner.TryGetPlayer(out var player) || !AccuracyDataLookup.TryGetValue(player.Lookup, out var accData))
        {
            return;
        }
        accData.Set(data);
        AccuracyDataNeedUpdate[player.Lookup] = true;
    }

    private void UpdateAccuracyData(int index, string text)
    {
        if (AccuracyTextMeshes.TryGetValue(index, out var textMesh))
        {
            textMesh.SetText(text);
            textMesh.ForceMeshUpdate();
        }
    }

    internal static void MarkAllAccuracyDataNeedUpdate()
    {
        foreach (var lookup in AccuracyDataNeedUpdate.Keys.ToList())
        {
            AccuracyDataNeedUpdate[lookup] = true;
        }
    }

    internal static void MarkAccuracyDataNeedUpdate(ulong lookup)
    {
        AccuracyDataNeedUpdate[lookup] = true;
    }

    internal static void DoClear()
    {
        foreach (var lookup in AccuracyDataLookup.Keys.ToList())
        {
            var data = AccuracyDataLookup[lookup];
            data.DoClear();
            AccuracyDataNeedUpdate[lookup] = true;
        }
    }

    internal static void SetVisible(int index, bool visible, bool update = true)
    {
        AccuracyTextMeshesVisible[index] = Enabled ? visible : false;
        if (update)
        {
            UpdateVisible();
        }
    }

    private static void UpdateVisible()
    {
        for (int i = 0; i < 4; i++)
        {
            if (!AccuracyTextMeshesVisible.ContainsKey(i))
            {
                continue;
            }
            int preInvisible = 0;
            for (int j = 0; j < 4; j++)
            {
                if (j <= i && !AccuracyTextMeshesVisible[j])
                {
                    preInvisible++;
                }
            }
            if (AccuracyTextMeshesVisible[i])
            {
                AccuracyTextMeshes[i].transform.parent.parent.gameObject.SetActive(true);
                AccuracyTextMeshes[i].transform.parent.parent.transform.localPosition = new(-70f + OffsetX, -62f + OffsetY + -35f * (i - preInvisible), 0f);
            }
            else
            {
                AccuracyTextMeshes[i].transform.parent.parent.gameObject.SetActive(false);
            }
        }
    }

    internal static void UnregisterAllPlayers()
    {
        foreach (var lookup in AccuracyRegisteredCharacterIndex.Keys)
        {
            UnregisterPlayer(lookup);
        }
        UpdateVisible();
    }

    internal static void UnregisterPlayer(ulong lookup)
    {
        if (AccuracyRegisteredCharacterIndex.TryGetValue(lookup, out var index))
        {
            SetVisible(index, false);
        }
        AccuracyDataLookup.Remove(lookup);
        AccuracyDataNeedUpdate.Remove(lookup);
        AccuracyRegisteredCharacterIndex.Remove(lookup);
    }

    internal static void AddHitted(ulong lookup, InventorySlot slot, uint count)
    {
        if (AccuracyDataLookup.TryGetValue(lookup, out var data))
        {
            data.AddHitted(slot, count);
        }
    }

    internal static void AddShotted(ulong lookup, InventorySlot slot, uint count)
    {
        if (AccuracyDataLookup.TryGetValue(lookup, out var data))
        {
            data.AddShotted(slot, count);
        }
    }

    internal static void AddWeakspotHitted(ulong lookup, InventorySlot slot, uint count)
    {
        if (AccuracyDataLookup.TryGetValue(lookup, out var data))
        {
            data.AddWeakspotHitted(slot, count);
        }
    }

    public static AccuracyUpdater Instance { get; private set; }

    public static int OffsetX
    {
        get
        {
            return _offsetX;
        }
        set
        {
            _offsetX = value;
            if (IsSetup)
            {
                UpdateVisible();
            }
        }
    }

    private static int _offsetX = 0;

    public static int OffsetY
    {
        get
        {
            return _offsetY;
        }
        set
        {
            _offsetY = value;
            if (IsSetup)
            {
                UpdateVisible();
            }
        }
    }

    private static int _offsetY = 0;

    public static bool Enabled
    {
        get
        {
            return _enable;
        }
        internal set
        {
            if (IsSetup)
            {
                MarkAllAccuracyDataNeedUpdate();
            }
            _enable = value;
        }
    }

    private static bool _enable = true;

    public static bool ShowOtherPlayersAcc
    {
        get
        {
            return _showOthersAcc;
        }
        internal set
        {
            if (IsSetup)
            {
                MarkAllAccuracyDataNeedUpdate();
            }
            _showOthersAcc = value;
        }
    }

    private static bool _showOthersAcc = true;

    public static bool ShowBotsAcc
    {
        get
        {
            return _showBotsAcc;
        }
        internal set
        {
            if (IsSetup)
            {
                MarkAllAccuracyDataNeedUpdate();
            }
            _showBotsAcc = value;
        }
    }

    private static bool _showBotsAcc = false;

    public static string ShowFormat
    {
        get
        {
            return _showFormat;
        }
        set
        {
            if (IsSetup)
            {
                MarkAllAccuracyDataNeedUpdate();
            }
            _showFormat = value;
        }
    }

    private static string _showFormat = "{0}: {1}/{2}({4}/{5})";
    
    public static string EndscreenFormat
    {
        get
        {
            return _endscreenFormat;
        }
        set
        {
            if (IsSetup)
            {
                MarkAllAccuracyDataNeedUpdate();
            }
            _endscreenFormat = value;
        }
    }
    private static string _endscreenFormat = "{0}/{1}({2}/{3}/{4})";

    public static bool UseGenericName
    {
        get
        {
            return _useGenericName;
        }
        set
        {
            if (IsSetup)
            {
                MarkAllAccuracyDataNeedUpdate();
            }
            _useGenericName = value;
        }
    }

    private static bool _useGenericName = true;

    public static bool TryGetPlayerAccuracyData(SNet_Player player, out AccuracyData data)
    {
        return AccuracyDataLookup.TryGetValue(player.Lookup, out data);
    }

    private static bool NeedShowAccuracy(SNet_Player player)
    {
        if (!Settings.Enabled || (!player.IsLocal && !ShowOtherPlayersAcc) || (player.IsBot && !ShowBotsAcc))
        {
            return false;
        }
        return IsAccuracyListener(player.Lookup) || player.IsLocal || (player.IsBot && IsMasterHasAcc) || IsMasterHasAcc;
    }

    public static Dictionary<int, PlayerNameEntry> CharacterNamesLookup { get; set; } = new()
    {
        { 0, new("Wood", "RED") }, { 1, new("Dauda", "GRE") }, { 2, new("Hackett", "BLU") }, { 3, new("Bishop", "PUR") }
    };

    public static bool IsSetup
    {
        get
        {
            return _isSetup;
        }
        private set
        {
            _isSetup = value;
        }
    }

    private static bool _isSetup;

    private static Dictionary<int, TextMeshPro> AccuracyTextMeshes { get; set; } = new();
    private static Dictionary<ulong, AccuracyData> AccuracyDataLookup { get; set; } = new();
    private static Dictionary<ulong, bool> AccuracyDataNeedUpdate { get; set; } = new();
    private static Dictionary<int, bool> AccuracyTextMeshesVisible { get; set; } = new();
    private static Dictionary<ulong, int> AccuracyRegisteredCharacterIndex { get; set; } = new();
    private static Dictionary<ulong, Coroutine> RegisterPlayerCoroutines = new();

    public class AccuracyData
    {
        internal AccuracyData(SNet_Player player)
        {
            Owner = player;
            m_SlotDataLookup[InventorySlot.GearStandard] = new();
            m_SlotDataLookup[InventorySlot.GearSpecial] = new();
        }

        internal void Set(pAccuracyData data)
        {
            data.Owner.TryGetPlayer(out var player);
            Owner = player;
            m_SlotDataLookup[InventorySlot.GearStandard].Set(data.StandardSlotData);
            m_SlotDataLookup[InventorySlot.GearSpecial].Set(data.SpecialSlotData);
        }

        internal void AddShotted(InventorySlot slot, uint count)
        {
            if (m_SlotDataLookup.TryGetValue(slot, out var data))
            {
                data.m_Shotted += count;
            }
        }

        internal void AddHitted(InventorySlot slot, uint count)
        {
            if (m_SlotDataLookup.TryGetValue(slot, out var data))
            {
                data.m_Hitted += count;
            }
        }

        internal void AddWeakspotHitted(InventorySlot slot, uint count)
        {
            if (m_SlotDataLookup.TryGetValue(slot, out var data))
            {
                data.m_WeakspotHitted += count;
            }
        }

        internal void DoClear()
        {
            foreach (var data in m_SlotDataLookup.Values)
            {
                data.DoClear();
            }
        }

        public SNet_Player Owner { get; private set; }
        public uint TotalHitted
        {
            get
            {
                var count = 0U;
                foreach (var slot in m_SlotDataLookup.Keys)
                {
                    count += m_SlotDataLookup[slot].m_Hitted;
                }
                return count;
            }
        }
        public uint TotalWeakspotHitted
        {
            get
            {
                var count = 0U;
                foreach (var slot in m_SlotDataLookup.Keys)
                {
                    count += m_SlotDataLookup[slot].m_WeakspotHitted;
                }
                return count;
            }
        }
        public uint TotalShotted
        {
            get
            {
                var count = 0U;
                foreach (var slot in m_SlotDataLookup.Keys)
                {
                    count += m_SlotDataLookup[slot].m_Shotted;
                }
                return count;
            }
        }

        private Dictionary<InventorySlot, AccuracySlotData> m_SlotDataLookup = new();

        public string GetAccuracyText()
        {
            if (!Owner.HasCharacterSlot)
            {
                return string.Format(Settings.ShowFormat, "-", "-", "-", 0, 0, 0);
            }
            string prefix = IsAccuracyListener(Owner.Lookup) || (IsMasterHasAcc && Owner.IsBot) || Owner.IsLocal ? "": "*";
            string playerName = UseGenericName ? CharacterNamesLookup[Owner.CharacterIndex].Name : Owner.NickName.RemoveHtmlTags();
            if (TotalShotted == 0)
            {
                return $"{prefix}{string.Format(Settings.ShowFormat, playerName, "-%", "-%", 0, 0, 0)}";
            }
            else
            {
                return $"{prefix}{string.Format(Settings.ShowFormat, playerName, $"<{Settings.FontColors.HittedRatioColor.ToHexString()}>{(int)(100 * TotalHitted / TotalShotted)}%</color>", TotalHitted == 0 ? "-" : $"<{Settings.FontColors.WeakspotHittedRatioColor.ToHexString()}>{(int)(100 * TotalWeakspotHitted / TotalHitted)}%</color>", $"<{Settings.FontColors.WeakspotHittedColor.ToHexString()}>{TotalWeakspotHitted}</color>", $"<{Settings.FontColors.HittedColor.ToHexString()}>{TotalHitted}</color>", $"<{Settings.FontColors.ShottedColor.ToHexString()}>{TotalShotted}</color>")}";
            }
        }

        public string GetAccuracyText(InventorySlot slot)
        {
            if (!Owner.HasCharacterSlot || !m_SlotDataLookup.TryGetValue(slot, out var data))
            {
                return string.Format(Settings.EndscreenFormat, "-", "-", 0, 0, 0);
            }
            string formatted;
            string prefix = IsAccuracyListener(Owner.Lookup) || (IsMasterHasAcc && Owner.IsBot) || Owner.IsLocal ? "" : "*";
            if (data.m_Shotted == 0)
            {
                formatted = $"{prefix}{string.Format(Settings.EndscreenFormat, "-%", "-%", 0, 0, 0)}";
            }
            else
            {
                formatted = $"{prefix}{string.Format(Settings.EndscreenFormat, $"<{Settings.FontColors.HittedRatioColor.ToHexString()}>{(int)(100 * data.m_Hitted / data.m_Shotted)}%</color>", TotalHitted == 0 ? "-" : $"<{Settings.FontColors.WeakspotHittedRatioColor.ToHexString()}>{(int)(100 * data.m_WeakspotHitted / data.m_Hitted)}%</color>", $"<{Settings.FontColors.WeakspotHittedColor.ToHexString()}>{data.m_WeakspotHitted}</color>", $"<{Settings.FontColors.HittedColor.ToHexString()}>{data.m_Hitted}</color>", $"<{Settings.FontColors.ShottedColor.ToHexString()}>{data.m_Shotted}</color>")}";
            }
            if (Settings.ShowColorsOnEndscreen)
            {
                return formatted;
            }
            return formatted.RemoveHtmlTags();
        }

        public pAccuracyData GetAccuracyData()
        {
            return new(Owner, m_SlotDataLookup);
        }

        internal class AccuracySlotData
        {
            internal void Set(pAccuracySlotData data)
            {
                m_Hitted = data.Hitted;
                m_Shotted = data.Shotted;
                m_WeakspotHitted = data.WeakspotHitted;
                m_Slot = data.Slot;
            }

            public void DoClear()
            {
                m_Hitted = 0;
                m_Shotted = 0;
                m_WeakspotHitted = 0;
            }

            public uint m_Hitted = 0;
            public uint m_Shotted = 0;
            public uint m_WeakspotHitted = 0;
            public InventorySlot m_Slot = InventorySlot.None;
        }
    }
}