# CLAUDE.md

Avatar Variant Selector — 1 つのシーンから複数のアップロード先ぶんのアバターをビルドする、VRChat 向けの NDMF プラグイン。

このリポジトリのルートは Unity プロジェクトだが、git が追跡しているのは `Packages/minminmean.avatar-variant-selector/` とルート直下のファイルだけ。VCC が管理する他パッケージ（VRChat SDK、Modular Avatar、NDMF など）は `Packages/.gitignore` で除外されている。編集対象は基本的にパッケージフォルダの中。

## コーディング規約

### 型

`var` は極力使わない。変数宣言には期待される型を明示する。

```csharp
// 良い例
List<GameObject> targets = new List<GameObject>();
AvatarVariantSet set = selector.VariantSet;

// 悪い例
var targets = new List<GameObject>();
var set = selector.VariantSet;
```

`foreach` のループ変数も対象に含める。コレクション側の宣言を追わなくても、読み下しただけで何が入っているか分かるようにするため。

```csharp
// 良い例
foreach (AvatarVariantDefinition v in Variants)

// 悪い例
foreach (var v in Variants)
```

例外は、匿名型を受ける場合のように `var` 以外に書きようがないケースだけ。

### 命名

クラス名は名詞にする。

| 良い例 | 悪い例 |
|---|---|
| `Initializer` | `Initialize` |

メソッド名は動詞にする。

| 良い例 | 悪い例 |
|---|---|
| `CreateAsset` | `AssetCreation` |

### 記述言語

| 対象 | 言語 |
|---|---|
| コード内のコメント | 日本語 |
| ローカライズできないデフォルト表示・エラーログ | 英語 |
| コミットメッセージ | 英語 |

`Debug.Log` / `Debug.LogError` などのログ出力や、ローカライズ辞書を通せないフォールバック文言は英語で書く。Inspector に出る表示文字列は `Editor/AvatarVariantSelector/Localize/ja.json` と `en.json` を通すこと。
