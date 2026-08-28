# RimTalk TTS Addon - Irodori Edition

RimWorld Mod **RimTalk** の会話を音声化する TTS アドオンです。

このリポジトリは [whatismyname0/RimTalkTTSModule](https://github.com/whatismyname0/RimTalkTTSModule) をベースに、**Irodori-TTS Server との連携を中心に拡張した Fork** です。

元の RimTalk TTS が持つ各種 TTS プロバイダ対応を維持しつつ、Irodori 向けに低遅延化、Voice Lab、BIO 画面からの音声管理などを追加しています。

## 主な機能

### RimTalk TTS 本来の機能

- RimTalk が生成した会話をリアルタイムで音声合成
- Pawn ごとの Voice 割り当て
- Voice Profile の追加・管理
- Fish Audio / CosyVoice / IndexTTS / EdgeTTS / Azure TTS / Gemini TTS / TTS WebUI などへの対応
- Pawn ごとの読み上げ言語設定
- 音量・速度などの調整
- Voice 割り当てルール

### Irodori Edition で追加した主な機能

- **Irodori-TTS Server 対応**
- **Fast Path**
  - RimTalk が生成した発話情報を直接 TTS に渡し、追加の前処理 LLM 呼び出しを可能な限り省略
  - 会話生成から読み上げ開始までの遅延を短縮
- **BIO Voice Preview**
  - BIO の Voice 選択画面から各 Voice をその場で試聴
  - 試聴用テキストを自由に編集可能
- **Voice Lab**
  - 最近の RimTalk 会話を元に Irodori の Voice Design を試行
  - Seed を固定・変更して複数候補を生成
  - 候補をその場で再生
  - 気に入った候補を Irodori Server に参照音声として登録
  - 登録後、自動的に RimTalk TTS の Voice Profile を作成して Pawn に割り当て
- **Voice 管理**
  - 試聴
  - 表示名の変更
  - 登録済み参照音声の削除
  - Voice Lab 生成 Voice は即削除
  - 手動配置 Voice は削除前に確認画面を表示
- **セーブ間の Voice 整合性修復**
  - 別セーブで削除された Voice を参照している古いセーブを読み込んだ場合、無効な直接割り当てを自動的に `Default` へ修復
- Irodori Voice ごとの Caption / パラメータ設定
- Preview 再生の排他制御
- Voice Lab 候補のメモリ保持数制限

## 必須 Mod

1. **Harmony**
   - Package ID: `brrainz.harmony`
2. **RimTalk**
   - Package ID: `cj.rimtalk`

推奨ロード順:

```text
Harmony
  ↓
RimTalk
  ↓
RimTalk TTS Addon - Irodori Edition
```

## Irodori-TTS Server

Irodori を使用する場合は、別途 **Irodori-TTS Server** が必要です。

- Server: [Aratako/Irodori-TTS-Server](https://github.com/Aratako/Irodori-TTS-Server)
- Model: [Aratako/Irodori-TTS-v4-Small](https://huggingface.co/Aratako/Irodori-TTS-v4-Small)

標準的な接続先は次の形式です。

```text
http://127.0.0.1:8088
```

RimTalk TTS の Mod 設定で TTS Supplier に `Irodori` を選択し、Irodori Server の Base URL を設定してください。

## 基本的な使い方

1. Harmony / RimTalk / この Mod を有効化
2. Mod 設定から TTS Supplier を `Irodori` に変更
3. Irodori Server の Base URL を設定
4. 接続確認
5. Pawn の BIO を開く
6. Voice 選択画面から Voice を選択
7. `▶` で試聴
8. 必要に応じて Voice Lab から新しい Voice を作成

### Voice 選択画面

Irodori Voice では、Voice 行の右側から次の操作を行えます。

```text
▶  試聴
✎  表示名を編集
×  削除
```

表示名を変更しても、内部の Voice ID は変更されません。

例:

```text
表示名: Harper Voice
Voice ID: rttts_953_20260827160448_fd310a
```

そのため、Pawn の割り当てや Irodori Server 上の参照音声との対応を壊さずに名前だけ整理できます。

## Voice Lab

Voice Lab では、Irodori の Voice Design 機能を使って参照音声候補を作成できます。

主な流れ:

```text
最近の RimTalk 会話を選択
        ↓
Voice Description / Delivery Hint を設定
        ↓
Seed を指定して音声生成
        ↓
候補を試聴
        ↓
気に入った候補を採用
        ↓
Irodori Server にアップロード
        ↓
Voice Profile 作成
        ↓
Pawn に割り当て
```

Voice Lab 内の未採用候補はメモリ上にのみ保持されます。候補数には上限があり、古い候補は自動的に破棄されます。

採用した Voice は Irodori Server 上に参照音声ファイルとして保存されるため、不要になった場合は Voice 管理画面から削除してください。

## Voice の削除

### Voice Lab で生成した Voice

`rttts_*` 形式の Voice は `×` を押すと削除されます。

### 手動配置した Voice

例:

```text
クール
ダネイ
voice_sample_01
```

手動 Voice は誤操作防止のため、削除ボタンを押した後に確認画面を表示します。

### 日本語 Voice ID の削除について

Irodori-TTS Server の一部バージョンでは、`クール` や `ダネイ` のような非 ASCII Voice ID を通常の音声合成では利用できる一方、DELETE API 側の Voice ID 検証によって削除要求が拒否される場合があります。

その場合は Irodori-TTS Server 側にも Unicode Voice ID の DELETE 対応が必要です。

## セーブデータと Voice 割り当て

Pawn ごとの Voice 割り当ては **RimWorld のセーブデータごと**に保存されます。

一方、Voice Profile は RimTalk TTS の Mod 設定として共有されます。

そのため、例えば:

```text
Save A で Voice X を削除
        ↓
Save B はまだ Voice X を Pawn に割り当てた状態
        ↓
Save B をロード
```

という状況が発生することがあります。

Irodori Edition では、ロード時に存在しない Voice Profile を直接参照している Pawn を検出し、その割り当てを `Default` へ自動修復します。

## ビルド

### 必要環境

- Windows
- .NET SDK
- RimWorld 1.6
- RimTalk

この Fork では `RimTalk.dll` の絶対パスを `.csproj` に保存しません。

リポジトリ直下から次のようにビルドできます。

```powershell
.\build.cmd -RimTalkDir "F:\SteamLibrary\steamapps\common\RimWorld\Mods\3551203752"
```

`-RimTalkDir` には RimTalk Mod のルートディレクトリを指定してください。

環境変数を使うこともできます。

```powershell
$env:RIMTALK_DIR = "F:\SteamLibrary\steamapps\common\RimWorld\Mods\3551203752"
.\build.cmd
```

生成先:

```text
1.6/Assemblies/RimTalk.TTS.dll
```

`RimTalk.TTS.dll` と `.pdb` はビルド生成物のため Git 管理対象外です。

## 開発ブランチ

- `main`
  - Fork 元への追従用
- `Irodori`
  - Irodori 統合版の開発ブランチ

Irodori v5.9 以降は、従来使用していた `apply_v5_xxx.py` のような差分パッチ方式ではなく、`Irodori` ブランチ上のソースを直接更新する方針です。

詳細は [IRODORI_DEVELOPMENT.md](IRODORI_DEVELOPMENT.md) を参照してください。

## 注意事項

### 本家 RimTalk TTS との同時使用

現在、この Fork は互換性維持のため本家と同じ Package ID を使用しています。

```text
nitoritech.rimtalk.tts
```

そのため、**本家 RimTalk TTS と Irodori Edition を同時に有効化しないでください。**

どちらか一方だけを有効化してください。

### 開発版について

この Fork は Irodori-TTS Server との連携機能を追加した開発版です。

本家 RimTalk TTS や Irodori-TTS Server の更新によって互換性が変化する可能性があります。

## クレジット

### RimTalk TTS

Original author: **Nitori_Tachyon**

Original repository:

[whatismyname0/RimTalkTTSModule](https://github.com/whatismyname0/RimTalkTTSModule)

### RimTalk

[jlibrary/RimTalk](https://github.com/jlibrary/RimTalk)

### Irodori-TTS Server

[Aratako/Irodori-TTS-Server](https://github.com/Aratako/Irodori-TTS-Server)

### Irodori Edition

Fork / Irodori integration: **Jyako03**

この Fork は元の RimTalk TTS のコードとライセンスを尊重し、その上に Irodori 向け機能を追加しています。

## ライセンス

ライセンスについては [LICENSE](LICENSE) を参照してください。
