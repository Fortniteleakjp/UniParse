# UniParse

[AssetRipper](https://github.com/AssetRipper/AssetRipper) をバックエンドに使った、**「FModel」の Unity 版**にあたるデスクトップ用アセットビューアです（WPF / .NET 10 / Windows）。

Unity の **AssetBundle・`.assets`・level・ゲームのデータフォルダ**などを開き、ファイル構造をツリー表示して、選んだアセットを **画像 / 3Dモデル / 音声 / 動画 / シェーダー / アニメーション / JSON** でプレビューできます。

---

## ⚠️ 免責事項・利用上の注意（必ずお読みください）

> **このツールは「学習・研究・解析」のための教育目的で提供されています。利用はすべて自己責任です。**

- 🟥 **自己責任での利用**：本ツールの使用によって生じた、いかなる損害・トラブル・データ損失・法的問題についても、**作者および貢献者は一切の責任を負いません**。
- 🟥 **著作権の尊重**：解析対象のゲーム・アプリ・アセットには著作権があります。**あなた自身が正当な権利を持つ、または解析が許諾されているデータのみ**を対象にしてください。
- 🟥 **再配布の禁止**：抽出した画像・音声・モデル・テキスト等を、**権利者の許諾なく再配布・公開・商用利用しないでください**。
- 🟥 **利用規約（ToS）の遵守**：各ゲーム/サービスの利用規約に反する使い方をしないでください。リバースエンジニアリングや解析が**禁止されている場合があります**。
- 🟥 **不正利用の禁止**：チート・改ざん・不正行為を目的としたツールではありません。そのような用途には使用しないでください。

> 上記に同意できない場合は本ツールを使用しないでください。**使用した時点で、これらに同意したものとみなします。**

---

## ✨ 主な機能

| 種別 | 内容 |
|------|------|
| 📂 開く | ファイル / フォルダ / 複数選択 / ドラッグ＆ドロップ。ゲームのデータフォルダ全体を開くと、ストリーミングリソース（`.resS`）も解決されます |
| 🌲 構造ツリー | `GameBundle → バンドル → コレクション → 型(Texture2D等) → アセット` |
| 🖼 画像 | `Texture2D / Sprite / Cubemap / Texture3D / Texture2DArray / TerrainData`（DXT / ETC / ASTC / BC / PVRTC / Crunch 等に対応）。ペインに自動フィット |
| 🎨 マテリアル | `Material` のメインテクスチャを画像表示 |
| 🧊 3Dモデル | `Mesh` を glTF 化して 3D 表示（ドラッグで回転 / ホイールでズーム）。`.glb` 保存 |
| 🔊 音声 | `AudioClip` をデコードして再生 / 停止。元形式で保存 |
| 🎬 動画 | `VideoClip` / `VideoPlayer` を再生（MediaElement）。元動画を保存 |
| 🧩 シェーダー | `Shader` の ShaderLab を表示（Dummy 出力） |
| 🎞 アニメーション | `AnimationClip` の概要（サンプルレート・カーブ数など）を表示 |
| `{ }` JSON | 全フィールドを **色付き**で表示（AvalonEdit + `DefaultJsonWalker`） |
| 🔎 検索 | **クラス（アセット種別）で絞り込み** + 名前フィルタ（AND 条件） |
| 💾 エクスポート | 画像(PNG) / 3D(glb) / 音声 / 動画 / JSON / テキストを保存 |
| 🔄 自動更新 | 起動時に GitHub Releases の最新版を確認し、バナーで通知 → ワンクリックでダウンロード→置換→再起動 |

---

## 📥 入手と実行（一般ユーザー向け）

ビルド不要で使う場合は、GitHub の **[Releases](https://github.com/Fortniteleakjp/Unity-analysis/releases)** から
`UniParse-win-x64.zip` をダウンロードし、展開して `UniParse.exe` を実行してください。

- **.NET のインストールは不要**です（自己完結型ビルド／ランタイム同梱）。
- 新しいバージョンが出ると、アプリ起動時に**緑のバナーで通知**されます。「⬇ 更新する」を押すと自動で更新できます。

---

## 🛠 ビルド（開発者向け）

```powershell
# 1) リポジトリ取得（AssetRipper は submodule）
git clone --recurse-submodules https://github.com/Fortniteleakjp/Unity-analysis.git
cd Unity-analysis

# 2) ビルド & 実行
dotnet build UniParse/UniParse.csproj -c Debug
dotnet run --project UniParse/UniParse.csproj
```

- 必要環境：**Windows 10/11 ＋ .NET 10 SDK**、初回はインターネット接続（NuGet 復元）。
- 初回ビルドは AssetRipper 全体をコンパイルするため数分かかります（2回目以降は差分ビルドで高速）。
- NuGet は `nuget.org` ＋ AssetRipper 独自フィード `https://nuget.samboy.dev`（`Disarm` 等）を使用します（リポジトリ直下の `nuget.config` で設定済み）。

### 自動ビルド & 配布（CI）

`main` ブランチへ push すると [GitHub Actions](.github/workflows/release.yml) が
**自己完結型 win-x64 ビルド**を作成し、**Release（`v1.0.<run番号>`）として自動配布**します。

---

## 🗂 構成

```
Unity-analysis/
├── .github/workflows/release.yml   ← CI（push で自動ビルド & Release 作成）
├── .gitmodules / nuget.config       ← submodule 設定 / NuGet フィード
├── external/AssetRipper/            ← AssetRipper 本体（submodule）
└── UniParse/
    ├── App.xaml(.cs)                ← ダークテーマ / 例外・アセンブリ解決ハンドラ
    ├── MainWindow.xaml(.cs)         ← UI（ツリー / プレビュー / タブ / 更新バナー）
    ├── Controls/Model3DViewer       ← 3D ビューア
    ├── Models/                      ← ツリーノード / クラス選択肢
    ├── Services/
    │   ├── UnityAssetService.cs     ← AssetRipper ラッパー（読み込み/各種プレビュー）
    │   └── UpdateService.cs         ← GitHub Releases チェック & 自動更新
    └── ViewModels/MainViewModel.cs
```

---

## 🧩 既知の制限

- IL2CPP ゲームの **スクリプト（C#）逆コンパイルは未対応**（構造・画像・JSON 表示は可能）。
- 動画は Windows 標準コーデック（Media Foundation）で再生できる形式のみ再生可能（webm/VP9・独自形式は保存のみ）。
- アニメーションは **概要表示**（実再生・スケルトン適用は未対応）。

---

## 🙏 クレジット

- 解析エンジン：**[AssetRipper](https://github.com/AssetRipper/AssetRipper)**（© ds5678 / 各ライセンスに従います）
- JSON 表示：**[AvalonEdit](https://github.com/icsharpcode/AvalonEdit)**

本ツールは AssetRipper を利用していますが、AssetRipper 公式とは無関係の非公式ツールです。
