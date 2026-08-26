using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace MinMinMart.AvatarVariant.Editor
{
    /// <summary>
    /// 表示文字列の辞書。フィールド名が ja.json / en.json のキーと 1 対 1 で対応する。
    /// {0} などを含むものは string.Format の書式として使う。
    /// </summary>
    [Serializable]
    public struct AvatarVariantLocalizeDictionary
    {
        public string language;
        public string profile_asset;
        public string profile_asset_help;
        public string create_profile_asset;
        public string no_avatar_root;
        public string no_pipeline_manager;

        public string build_target;
        public string build_target_none;
        public string blueprint_id;
        public string blueprint_id_unassigned;
        public string blueprint_id_new_upload;
        public string blueprint_id_line;

        public string switch_header;
        public string switch_to;
        public string switch_current;
        public string switch_no_variants;
        public string mark_new_upload;
        public string pending_banner;

        public string variants_header;
        public string variant_unnamed;
        public string placeholder_name;
        public string placeholder_id;
        public string duplicate;
        public string delete;
        public string delete_dialog_title;
        public string delete_dialog_message;
        public string delete_dialog_cancel;
        public string operations_header;
        public string add_variant;
        public string copy_suffix;

        public string op_remove;
        public string op_material;
        public string op_blendshape;
        public string field_slot;
        public string field_material;
        public string field_shape;
        public string field_value;
        public string add_entry;
        public string drop_area;
        public string path_broken;
        public string shape_none;
        public string shape_missing_suffix;
        public string outside_avatar_pick;
        public string outside_avatar_drop;

        public string warn_no_variants;
        public string warn_no_selection;
        public string warn_duplicate_id;
        public string warn_remove_missing;
        public string warn_material_missing;
        public string warn_material_slot;
        public string warn_shape_no_renderer;
        public string warn_shape_unselected;
        public string warn_shape_missing;

        public string log_switched;
        public string log_marked_pending;
        public string log_adopted_blueprint_id;
        public string log_created_asset;
        public string log_wrote_back;

        public string asset_edit_hint;
        public string asset_select_user;
        public string asset_unnamed;
        public string asset_operations;
        public string asset_operations_value;
        public string asset_pending;
        public string asset_user_not_found;

        public string build_multiple_selectors;
        public string build_no_profile;
        public string build_cannot_resolve;
        public string build_hint_switch;
        public string build_via_pending;
        public string build_no_selection;
        public string build_remove_missing;
        public string build_target_missing;
        public string build_no_renderer;
        public string build_no_skinned_renderer;
        public string build_slot_out_of_range;
        public string build_shape_missing;
        public string build_done;
    }

    /// <summary>
    /// 辞書の読み込みと言語選択。
    ///
    /// 辞書フォルダはこのスクリプト自身の位置から引く。定数のパスも GUID も持たないので、
    /// フォルダごと移動しても、別プロジェクトへ持ち出しても壊れない。
    /// 読み込んだ辞書は言語ごとにキャッシュし、毎フレーム読み直さない。
    /// </summary>
    public static class AvatarVariantLocalize
    {
        public static readonly string[] LanguageNames = { "日本語", "English" };
        private static readonly string[] LocalizeFiles = { "ja.json", "en.json" };

        private const string LanguagePrefKey = "MinMinMart.AvatarVariant.Language";

        // EditorPrefs はレジストリを読むため、表示のたびに引くと積み重なって重くなる。
        // ドメインリロードごとに 1 回だけ読み、以降はこの値を使う。
        private static int _languageIndex = -1;

        private static int _cachedIndex = -1;
        private static AvatarVariantLocalizeDictionary _cached;

        /// <summary>
        /// 選択中の言語。既定はエディタのシステム言語から決める。
        /// </summary>
        public static int LanguageIndex
        {
            get
            {
                if (_languageIndex < 0)
                {
                    int fallback = Application.systemLanguage == SystemLanguage.Japanese ? 0 : 1;
                    _languageIndex = Mathf.Clamp(EditorPrefs.GetInt(LanguagePrefKey, fallback),
                        0, LocalizeFiles.Length - 1);
                }

                return _languageIndex;
            }

            set
            {
                int clamped = Mathf.Clamp(value, 0, LocalizeFiles.Length - 1);
                if (clamped == LanguageIndex) return;

                EditorPrefs.SetInt(LanguagePrefKey, clamped);
                _languageIndex = clamped;
                _cachedIndex = -1;
            }
        }

        /// <summary>
        /// 選択中の言語の辞書。
        /// </summary>
        public static AvatarVariantLocalizeDictionary Dictionary
        {
            get
            {
                int index = LanguageIndex;
                if (_cachedIndex == index) return _cached;

                _cached = Load(index);
                _cachedIndex = index;
                return _cached;
            }
        }

        /// <summary>
        /// 言語切り替えのポップアップ。
        /// </summary>
        public static void DrawLanguagePopup()
        {
            EditorGUI.BeginChangeCheck();
            int picked = EditorGUILayout.Popup(Dictionary.language, LanguageIndex, LanguageNames);
            if (EditorGUI.EndChangeCheck())
            {
                LanguageIndex = picked;
            }
        }

        private static AvatarVariantLocalizeDictionary Load(int index)
        {
            string folder = FindLocalizeFolder();
            if (folder == null)
            {
                Debug.LogError("[Avatar Variant] Localize folder not found.");
                return FillMissingStrings(default);
            }

            TextAsset asset = AssetDatabase.LoadAssetAtPath<TextAsset>($"{folder}/{LocalizeFiles[index]}");
            if (asset == null)
            {
                Debug.LogError($"[Avatar Variant] Failed to load {folder}/{LocalizeFiles[index]}.");
                return FillMissingStrings(default);
            }

            return FillMissingStrings(JsonUtility.FromJson<AvatarVariantLocalizeDictionary>(asset.text));
        }

        /// <summary>
        /// 値が入らなかったフィールドを空文字で埋める。
        ///
        /// 辞書は構造体なので、読み込みに失敗すると全フィールドが null になる。
        /// そのまま string.Format に渡すと例外になり、Inspector が描けなくなる。
        /// キーが 1 つ欠けただけの場合も同じなので、読み込み経路の最後で必ず通す。
        /// </summary>
        private static AvatarVariantLocalizeDictionary FillMissingStrings(AvatarVariantLocalizeDictionary dictionary)
        {
            // 構造体なのでボックス化しないとリフレクションで書き戻せない。
            object boxed = dictionary;
            foreach (FieldInfo field in typeof(AvatarVariantLocalizeDictionary).GetFields())
            {
                if (field.FieldType != typeof(string)) continue;
                if (field.GetValue(boxed) != null) continue;

                field.SetValue(boxed, "");
            }

            return (AvatarVariantLocalizeDictionary)boxed;
        }

        /// <summary>
        /// このスクリプトの場所を起点に Localize フォルダを探す。
        /// 同じ階層と 1 つ上の階層を見るので、パッケージ配置でも Assets 直置きでも動く。
        /// </summary>
        private static string FindLocalizeFolder()
        {
            string scriptPath = AssetDatabase.FindAssets($"t:MonoScript {nameof(AvatarVariantLocalize)}")
                .Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(p => Path.GetFileNameWithoutExtension(p) == nameof(AvatarVariantLocalize));

            if (string.IsNullOrEmpty(scriptPath)) return null;

            string scriptDir = Path.GetDirectoryName(scriptPath);
            foreach (string dir in new string[] { scriptDir, Path.GetDirectoryName(scriptDir) })
            {
                if (string.IsNullOrEmpty(dir)) continue;

                string candidate = (dir + "/Localize").Replace('\\', '/');
                if (AssetDatabase.IsValidFolder(candidate)) return candidate;
            }

            return null;
        }
    }
}
