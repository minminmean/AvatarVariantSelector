using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MinMinMart.AvatarVariant.Editor
{
    /// <summary>
    /// コンポーネントで編集した内容をプロファイルアセットへ書き出す。
    ///
    /// 操作のたびに書き出すと再インポートが走って重いので、編集中は変更済みの印だけを付け、
    /// 書き出しはここに集めたきっかけでだけ行う。UI に「適用」ボタンは出さず、
    /// 区切りの良いところで自動的に確定させる。
    ///
    /// きっかけは4つ。操作の登録（追加・削除・対象の指定）、名前と Blueprint ID の
    /// 入力欄からフォーカスが外れたとき、ビルド、シーンの保存。
    /// </summary>
    [InitializeOnLoad]
    internal static class AvatarVariantProfileSaver
    {
        // 描画の途中で書き出すと、そのフレームの入力を取りこぼす。
        // 要求だけ受けておき、SerializedObject を反映し終えてから書き出す。
        private static bool _requested;

        // フォーカスが外れたら書き出す欄に付ける名前の接頭辞。
        private const string WatchedFieldPrefix = "avatar-variant-watched-";

        // 直前にフォーカスが当たっていたコントロール。
        private static string _focusedField = "";

        static AvatarVariantProfileSaver()
        {
            EditorSceneManager.sceneSaved += SaveAllInScene;
        }

        /// <summary>
        /// 次に描くコントロールを、フォーカスが外れた時点で書き出す対象として名付ける。
        /// </summary>
        internal static void NameWatchedField(string id)
        {
            GUI.SetNextControlName(WatchedFieldPrefix + id);
        }

        /// <summary>
        /// 名付けた欄からフォーカスが外れていたら、書き出しを頼む。
        ///
        /// 入力のたびに書き出すと重いので、打ち終わりの目印としてフォーカスの移動を使う。
        /// </summary>
        internal static void RequestOnFocusLost()
        {
            string focused = GUI.GetNameOfFocusedControl();
            if (focused == _focusedField) return;

            if (_focusedField.StartsWith(WatchedFieldPrefix))
            {
                Request();
            }

            _focusedField = focused;
        }

        /// <summary>
        /// 確定させたい操作があったことを覚えておく。
        /// </summary>
        internal static void Request()
        {
            _requested = true;
        }

        /// <summary>
        /// 覚えがあれば書き出す。SerializedObject を反映し終えてから呼ぶこと。
        /// </summary>
        internal static void SaveIfRequested(AvatarVariantProfile profile)
        {
            if (!_requested) return;

            _requested = false;
            Save(profile);
        }

        /// <summary>
        /// その場で書き出す。変更が無ければ何も起きない。
        /// </summary>
        internal static void Save(AvatarVariantProfile profile)
        {
            if (profile == null) return;

            AssetDatabase.SaveAssetIfDirty(profile);
        }

        /// <summary>
        /// 保存されたシーンに置かれているコンポーネントのプロファイルアセットをすべて書き出す。
        /// </summary>
        private static void SaveAllInScene(Scene scene)
        {
            foreach (AvatarVariantSelector selector in Object.FindObjectsOfType<AvatarVariantSelector>(true))
            {
                if (selector.gameObject.scene != scene) continue;

                Save(selector.Profile);
            }
        }
    }
}
