using System;
using System.Collections.Generic;
using System.Windows.Forms;
using Rage;
using Rage.Attributes;
using Rage.Native;
using RAGENativeUI;
using RAGENativeUI.Elements;
using RAGENativeUI.PauseMenu;

[assembly: Plugin("Outfit Toggler", Description = "Allows you to toggle between EUP clothing | Made by Debugg#8770.", Author = "Debugg")]
namespace outfitToggler
{
    public static class EntryPoint
    {
        #region Variables
        private static MenuPool _pool;
        private static UIMenu _mainMenu;
        private static Keys _keyBinding;
        private static Keys _gloveKey;
        private static Keys _glassKey;
        private static Keys _modifierKey;
        private static bool _isToggle;
        private static bool _enableQuickKey;
        private static bool _useModifierKey;

        // Config values (loaded from ini)
        private static int _mShoes, _fShoes, _mPants, _fPants, _undershirt, _fShirt, _mShirt;

        // Lookup maps (loaded from XML)
        private static OutfitMappingsData _mappings = new();

        // Last-known prop state
        private static int _lastHatDraw,      _lastHatTex;
        private static int _lastGlassesDraw,  _lastGlassesTex;
        private static int _lastEarDraw,      _lastEarTex;
        private static int _lastWatchDraw,    _lastWatchTex;
        private static int _lastBraceletDraw, _lastBraceletTex;
        private static int _lastVisorDraw,    _lastVisorTex;

        // Last-known variation state
        private static int _lastMaskDraw,       _lastMaskTex;
        private static int _lastBagDraw,        _lastBagTex;
        private static int _lastBagsDraw,       _lastBagsTex;
        private static int _lastHairDraw,       _lastHairTex;
        private static int _lastNeckDraw,       _lastNeckTex;
        private static int _lastVestDraw,       _lastVestTex;
        private static int _lastShoesDraw,      _lastShoesTex;
        private static int _lastPantsDraw,      _lastPantsTex;
        private static int _lastGlovesDraw,     _lastGlovesTex;
        private static int _lastJacketsDraw,    _lastJacketsTex;
        private static int _lastShirtDraw,      _lastShirtTex;
        private static int _lastUndershirtDraw, _lastUndershirtTex;
        private static int _lastDecalDraw,      _lastDecalTex;

        // Maps menu item text to a clothing slot ID
        private static readonly Dictionary<string, int> MenuItemMap = new()
        {
            { "Hair",     1  },
            { "Bag",      2  },
            { "Glasses",  3  },
            { "Ear",      4  },
            { "Necklace", 5  },
            { "Bracelet", 6  },
            { "Watch",    7  },
            { "Vest",     8  },
            { "Mask",     9  },
            { "Shoes",    10 },
            { "Hat",      11 },
            { "Gloves",   12 },
            { "Pants",    13 },
            { "Shirt",    14 },
            { "Visor",    15 },
            { "Jacket",   16 },
            { "Bag O/C",  17 },
        };
        #endregion

        #region Initialisation
        private static InitializationFile InitialiseFile()
        {
            var ini = new InitializationFile("Plugins/outfitSettings.ini");
            ini.Create();
            return ini;
        }

        private static void LoadKeybindings()
        {
            var ini = InitialiseFile();
            var kc = new KeysConverter();

            _isToggle       = ini.ReadBoolean("Keybindings", "ToggleKey",       true);
            _enableQuickKey = ini.ReadBoolean("Keybindings", "EnableQuickKey",  true);
            _useModifierKey = ini.ReadBoolean("Keybindings", "UseModifierKey",  true);

            _keyBinding  = ParseKey(kc, ini.ReadString("Keybindings", "openMenuBinding", "F6"));
            _modifierKey = ParseKey(kc, ini.ReadString("Keybindings", "ModifierKey",     "LShiftKey"));
            _glassKey    = ParseKey(kc, ini.ReadString("Keybindings", "toggGlasses",     "V"));
            _gloveKey    = ParseKey(kc, ini.ReadString("Keybindings", "toggGloves",      "G"));
        }

        private static Keys ParseKey(KeysConverter kc, string value) =>
            (Keys)kc.ConvertFromString(value)!;

        private static void LoadConfig()
        {
            var ini = InitialiseFile();
            _mShoes     = ini.ReadInt32("Clothing", "mShoes",     34);
            _fShoes     = ini.ReadInt32("Clothing", "fShoes",     35);
            _mPants     = ini.ReadInt32("Clothing", "mPants",     61);
            _fPants     = ini.ReadInt32("Clothing", "fPants",     14);
            _undershirt = ini.ReadInt32("Clothing", "Undershirt", 15);
            _fShirt     = ini.ReadInt32("Clothing", "fShirt",     74);
            _mShirt     = ini.ReadInt32("Clothing", "mShirt",     252);
        }

        private static void LoadMappings()
        {
            const string mappingsPath = "Plugins/outfitMappings.xml";

            try
            {
                _mappings = OutfitMappingsLoader.Load(mappingsPath);
            }
            catch (Exception ex)
            {
                _mappings = new OutfitMappingsData();
                Notify($"~r~Outfit mapping XML load failed: {ex.Message}");
            }
        }
        #endregion

        #region Entry point
        public static void Main()
        {
            LoadKeybindings();
            LoadConfig();
            LoadMappings();
            BuildMenu();
            GameFiber.StartNew(ProcessMenus);
        }

        private static void BuildMenu()
        {
            _pool      = new MenuPool();
            _mainMenu  = new UIMenu("Outfit Menu", "Select an option to toggle it.");

            foreach (var label in MenuItemMap.Keys)
            {
                _mainMenu.AddItem(new UIMenuItem(label));
            }

            _mainMenu.OnItemSelect += (_, item, _) =>
            {
                if (MenuItemMap.TryGetValue(item.Text, out int id))
                    SwitchClothing(id);
                else
                    Game.DisplayNotification("An error occurred???");
            };

            _pool.Add(_mainMenu);
        }
        #endregion

        #region Helpers
        private static bool IsMpPed()
        {
            uint model = GetPedModel();
            return model == GetHash("mp_f_freemode_01") || model == GetHash("mp_m_freemode_01");
        }

        private static bool IsFemale() =>
            GetPedModel() == GetHash("mp_f_freemode_01");

        private static uint GetPedModel() =>
            NativeFunction.Natives.GetEntityModel<uint>(Game.LocalPlayer.Character);

        private static uint GetHash(string name) =>
            NativeFunction.Natives.GetHashKey<uint>(name);

        private static void PlayAnim(string dict, string anim, int flags, int duration)
        {
            var ped = Game.LocalPlayer.Character;
            ped.Tasks.PlayAnimation(dict, anim, 3.0f, (AnimationFlags)flags);
            GameFiber.Wait(duration);
            ped.Tasks.ClearSecondary();
        }

        private static void Notify(string msg) => Game.DisplayNotification(msg);

        private static void NothingToToggle() =>
            Notify("You dont appear to have anything to toggle.");
        #endregion

        #region Toggle helpers

        // --- Props (hat, glasses, ear, watch, bracelet) ---
        private static bool ToggleProp(int propSlot, ref int lastDraw, ref int lastTex)
        {
            var ped         = Game.LocalPlayer.Character;
            int currentProp = NativeFunction.Natives.GetPedPropIndex<int>(ped, propSlot);
            int currentTex  = NativeFunction.Natives.GetPedPropTextureIndex<int>(ped, propSlot);

            if (currentProp == -1 && lastDraw == 0)
            {
                NothingToToggle();
                return false;
            }

            if (currentProp == -1)
            {
                NativeFunction.Natives.SetPedPropIndex(ped, propSlot, lastDraw, lastTex, true);
                lastDraw = 0;
                lastTex  = 0;
            }
            else
            {
                lastDraw = currentProp;
                lastTex  = currentTex;
                NativeFunction.Natives.ClearPedProp(ped, propSlot);
            }

            return true;
        }

        // --- Simple on/off variation (mask, bag, necklace, vest) ---
        private static bool ToggleVariationSimple(int comp, ref int lastDraw, ref int lastTex, int offValue = 0)
        {
            var ped = Game.LocalPlayer.Character;
            ped.GetVariation(comp, out int draw, out int tex);

            if ((draw == -1 || draw == offValue) && lastDraw == 0)
            {
                NothingToToggle();
                return false;
            }

            if (draw == -1 || draw == offValue)
            {
                ped.SetVariation(comp, lastDraw, lastTex);
                lastDraw = 0;
                lastTex  = 0;
            }
            else
            {
                lastDraw = draw;
                lastTex  = tex;
                ped.SetVariation(comp, offValue, 0);
            }

            return true;
        }

        // --- Gender-value variation (shoes, pants) ---
        private static bool ToggleVariationGender(int comp, int maleOff, int femaleOff,
            ref int lastDraw, ref int lastTex)
        {
            int offValue = IsFemale() ? femaleOff : maleOff;
            var ped      = Game.LocalPlayer.Character;
            ped.GetVariation(comp, out int draw, out int tex);

            if (draw == offValue && lastDraw == 0)
            {
                NothingToToggle();
                return false;
            }

            if (draw == offValue)
            {
                ped.SetVariation(comp, lastDraw, lastTex);
                lastDraw = 0;
                lastTex  = 0;
            }
            else
            {
                lastDraw = draw;
                lastTex  = tex;
                ped.SetVariation(comp, offValue, 0);
            }

            return true;
        }

        // --- Dictionary-mapped variation (gloves, jackets, bags open/close) ---
        private static bool ToggleVariationMapped(int comp,
            Dictionary<int, int> maleMap, Dictionary<int, int> femaleMap,
            ref int lastDraw, ref int lastTex)
        {
            var ped      = Game.LocalPlayer.Character;
            var map      = IsFemale() ? femaleMap : maleMap;
            ped.GetVariation(comp, out int draw, out int tex);

            if (!map.ContainsKey(draw) && lastDraw == 0)
            {
                NothingToToggle();
                return false;
            }

            if (!map.ContainsKey(draw) && lastDraw != 0)
            {
                ped.SetVariation(comp, lastDraw, lastTex);
                lastDraw = 0;
                lastTex  = 0;
                return true;
            }

            if (map.ContainsKey(draw) && lastDraw == 0)
            {
                lastDraw = draw;
                lastTex  = tex;
                ped.SetVariation(comp, map[draw], 0);
                return true;
            }

            NothingToToggle();
            return false;
        }

        // --- Dictionary-mapped prop (visor) ---
        private static bool TogglePropMapped(int propSlot,
            Dictionary<int, int> maleMap, Dictionary<int, int> femaleMap,
            ref int lastDraw, ref int lastTex)
        {
            var ped      = Game.LocalPlayer.Character;
            var map      = IsFemale() ? femaleMap : maleMap;
            int current  = NativeFunction.Natives.GetPedPropIndex<int>(ped, propSlot);
            int currentTex = NativeFunction.Natives.GetPedPropTextureIndex<int>(ped, propSlot);

            if (!map.ContainsKey(current) && lastDraw == 0)
            {
                NothingToToggle();
                return false;
            }

            if (!map.ContainsKey(current) && lastDraw != 0)
            {
                NativeFunction.Natives.SetPedPropIndex(ped, propSlot, lastDraw, lastTex, true);
                lastDraw = 0;
                lastTex  = 0;
                return true;
            }

            if (map.ContainsKey(current) && lastDraw == 0)
            {
                lastDraw = current;
                lastTex  = currentTex;
                NativeFunction.Natives.SetPedPropIndex(ped, propSlot, map[current], currentTex, true);
                return true;
            }

            NothingToToggle();
            return false;
        }

        // --- Shirt (also toggles undershirt + decal) ---
        private static bool ToggleShirt()
        {
            var ped      = Game.LocalPlayer.Character;
            int offValue = IsFemale() ? _fShirt : _mShirt;
            ped.GetVariation(11, out int draw, out int tex);

            if (draw == offValue && _lastShirtDraw == 0)
            {
                NothingToToggle();
                return false;
            }

            if (draw == offValue)
            {
                ped.SetVariation(11, _lastShirtDraw,      _lastShirtTex);
                ped.SetVariation(8,  _lastUndershirtDraw, _lastUndershirtTex);
                ped.SetVariation(10, _lastDecalDraw,      _lastDecalTex);
                _lastShirtDraw      = 0; _lastShirtTex      = 0;
                _lastUndershirtDraw = 0; _lastUndershirtTex = 0;
                _lastDecalDraw      = 0; _lastDecalTex      = 0;
            }
            else
            {
                ped.GetVariation(8,  out int usDraw,    out int usTex);
                ped.GetVariation(10, out int decalDraw, out int decalTex);
                _lastShirtDraw      = draw;      _lastShirtTex      = tex;
                _lastUndershirtDraw = usDraw;    _lastUndershirtTex = usTex;
                _lastDecalDraw      = decalDraw; _lastDecalTex      = decalTex;
                ped.SetVariation(11, offValue,      0);
                ped.SetVariation(8,  _undershirt,   0);
                ped.SetVariation(10, 0,             0);
            }

            return true;
        }

        // --- Hair ---
        private static bool ToggleHair()
        {
            var ped = Game.LocalPlayer.Character;
            var map = IsFemale() ? _mappings.Hair.Female : _mappings.Hair.Male;
            ped.GetVariation(2, out int draw, out int tex);

            if (!map.ContainsKey(draw) && _lastHairDraw == 0)
            {
                NothingToToggle();
                return false;
            }

            if (!map.TryGetValue(draw, out int value))
            {
                ped.SetVariation(2, _lastHairDraw, _lastHairTex);
                _lastHairDraw = 0;
                _lastHairTex = 0;
                return true;
            }

            if (_lastHairDraw == 0)
            {
                _lastHairDraw = draw;
                _lastHairTex = tex;
                ped.SetVariation(2, value, tex);
                return true;
            }

            NothingToToggle();
            return false;
        }
        #endregion

        #region SwitchClothing dispatcher
        private static void SwitchClothing(int id)
        {
            switch (id)
            {
                case 1:  // Hair
                    if (ToggleHair())
                        PlayAnim("clothingtie", "check_out_a", 51, 2000);
                    break;

                case 2:  // Bag (on/off)
                    if (ToggleVariationSimple(5, ref _lastBagDraw, ref _lastBagTex))
                        PlayAnim("clothingtie", "try_tie_negative_a", 51, 1600);
                    break;

                case 3:  // Glasses
                    if (ToggleProp(1, ref _lastGlassesDraw, ref _lastGlassesTex))
                        PlayAnim("clothingspecs", "take_off", 51, 1400);
                    break;

                case 4:  // Ear
                    if (ToggleProp(2, ref _lastEarDraw, ref _lastEarTex))
                        PlayAnim("mp_cp_stolen_tut", "b_think", 51, 900);
                    break;

                case 5:  // Necklace
                    if (ToggleVariationSimple(7, ref _lastNeckDraw, ref _lastNeckTex))
                        PlayAnim("clothingtie", "try_tie_positive_a", 51, 2100);
                    break;

                case 6:  // Bracelet
                    if (ToggleProp(7, ref _lastBraceletDraw, ref _lastBraceletTex))
                        PlayAnim("nmt_3_rcm-10", "cs_nigel_dual-10", 51, 1200);
                    break;

                case 7:  // Watch
                    if (ToggleProp(6, ref _lastWatchDraw, ref _lastWatchTex))
                        PlayAnim("nmt_3_rcm-10", "cs_nigel_dual-10", 51, 1200);
                    break;

                case 8:  // Vest
                    if (ToggleVariationSimple(9, ref _lastVestDraw, ref _lastVestTex))
                        PlayAnim("clothingtie", "try_tie_negative_a", 51, 1200);
                    break;

                case 9:  // Mask
                    if (ToggleVariationSimple(1, ref _lastMaskDraw, ref _lastMaskTex))
                        PlayAnim("anim@heists@ornate_bank@grab_cash", "intro", 51, 800);
                    break;

                case 10: // Shoes
                    if (ToggleVariationGender(6, _mShoes, _fShoes, ref _lastShoesDraw, ref _lastShoesTex))
                        PlayAnim("random@domestic", "pickup_low", 0, 1200);
                    break;

                case 11: // Hat
                    if (ToggleProp(0, ref _lastHatDraw, ref _lastHatTex))
                        PlayAnim("mp_masks@standard_car@ds@", "put_on_mask", 51, 600);
                    break;

                case 12: // Gloves
                    if (ToggleVariationMapped(3, _mappings.Gloves.Male, _mappings.Gloves.Female, ref _lastGlovesDraw, ref _lastGlovesTex))
                        PlayAnim("nmt_3_rcm-10", "cs_nigel_dual-10", 51, 1200);
                    break;

                case 13: // Pants
                    if (ToggleVariationGender(4, _mPants, _fPants, ref _lastPantsDraw, ref _lastPantsTex))
                        PlayAnim("re@construction", "out_of_breath", 51, 1300);
                    break;

                case 14: // Shirt
                    if (ToggleShirt())
                        PlayAnim("clothingtie", "try_tie_negative_a", 51, 1200);
                    break;

                case 15: // Visor (prop slot 0)
                    if (TogglePropMapped(0, _mappings.Visor.Male, _mappings.Visor.Female, ref _lastVisorDraw, ref _lastVisorTex))
                        PlayAnim("mp_masks@standard_car@ds@", "put_on_mask", 51, 600);
                    break;

                case 16: // Jacket
                    if (ToggleVariationMapped(11, _mappings.Jackets.Male, _mappings.Jackets.Female, ref _lastJacketsDraw, ref _lastJacketsTex))
                        PlayAnim("missmic4", "michael_tux_fidget", 51, 1500);
                    break;

                case 17: // Bag open/close
                    if (ToggleVariationMapped(5, _mappings.Bags.Male, _mappings.Bags.Female, ref _lastBagsDraw, ref _lastBagsTex))
                        PlayAnim("anim@heists@ornate_bank@grab_cash", "intro", 51, 1600);
                    break;

                default:
                    Notify("The fuck have you done?");
                    break;
            }
        }
        #endregion

        #region Menu processor
        private static void ProcessMenus()
        {
            while (true)
            {
                GameFiber.Yield();
                _pool.ProcessMenus();

                if (!UIMenu.IsAnyMenuVisible && _enableQuickKey)
                {
                    bool modifierSatisfied = !_useModifierKey ||
                        Game.GetKeyboardState().IsDown(_modifierKey);

                    if (modifierSatisfied)
                    {
                        if (Game.IsKeyDown(_gloveKey)) SwitchClothing(12);
                        if (Game.IsKeyDown(_glassKey)) SwitchClothing(3);
                    }
                }

                bool menuKeyPressed = _isToggle
                    ? Game.IsKeyDown(_keyBinding)
                    : Game.IsKeyDownRightNow(_keyBinding);

                if (!UIMenu.IsAnyMenuVisible && !TabView.IsAnyPauseMenuVisible && menuKeyPressed)
                {
                    if (IsMpPed())
                    {
                        _mainMenu.Visible = true;
                        _mainMenu.MouseControlsEnabled = false;
                    }
                    else
                    {
                        Notify("~r~~h~Error~h~~s~: Switch to a MP ped to open this menu!!");
                    }
                }
                else if (!_isToggle && !Game.IsKeyDownRightNow(_keyBinding))
                {
                    _mainMenu.Visible = false;
                }
            }
            // ReSharper disable once FunctionNeverReturns
        }
        #endregion
    }
}