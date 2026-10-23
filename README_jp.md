# MikuSB

<strong>MikuSB</strong>は、あるダンジョンアニメゲームのサーバーエミュレーターです。  
`SdkServer`、`GameServer`、任意のローカル HTTP/HTTPS プロキシを 1 つの `net10.0` アプリとして起動します。  

[Discord](https://discord.gg/6MvWY774hX)

[English](README.md) | [日本語](README_jp.md)

## 詐欺に関する警告

MikuSB は完全無料のオープンソースです。  
このサーバーを誰かから有料で販売された場合、それは詐欺です。  
すぐに返金を申請し、購入記録や証拠とあわせて Discord で私たちに通報してください。  

## 概要

- `SdkServer`
  - HTTP API とディスパッチを返します
  - サーバー一覧、バージョン照会、各種フォールバックレスポンスを返します
  - 特定のチャネル向けにバージョン `3.6.0` を返します
- `GameServer`
  - TCP ベースのゲーム接続を受けます
  - `ReqCallGS` と、対応済みシステムのゲームパケットを処理します
- `Proxy`
  - 有効時のみ `127.0.0.1:18888` で待ち受けます
  - 一部の Snowbreak 関連ドメインをローカル `SdkServer` へリダイレクトします
- `Common` / `Proto` / `TcpSharp`
  - 共通データ、protobuf 定義、通信基盤です

## プロジェクト構成

- [MikuSB](MikuSB): エントリーポイント
- [SdkServer](SdkServer): HTTP サーバーとディスパッチ
- [GameServer](GameServer): ゲームサーバー本体
- [Proxy](Proxy): ローカルプロキシ
- [Common](Common): 設定、DB、共通処理
- [Proto](Proto): protobuf 定義

## 要件

- [.NET SDK 10.0](https://dotnet.microsoft.com/ja-jp/download/dotnet/10.0)

## 起動方法

1. 依存を復元してビルドします。
```powershell
dotnet build
```
2. Config.json の`GamePath`にあなたのゲームの実行ファイルのパスを書き込みます  
3. サーバーを起動し`game`コマンドを入力します  
4. サーバーコンソールでアカウントを作成する  
5. 楽しむ

### ローカル上書き

`Config/Config.json` は git 管理下にあるため、`GamePath` や `Arguments`、LAN アドレスなどの環境依存値は `Config/Config.Local.json` に書いてください。起動時に `Config.json` へマージされ、このファイルが存在する間は `Config.json` への書き戻しは行われません。

## リリースビルド

```powershell
pwsh -File build.ps1
```

`build/MikuSB-win-x64/`、`build/MikuSB-win-x64.zip`、`build/MikuSB-win-x64.zip.sha256` を出力します。ローカルビルドのアセンブリにはビルド元の絶対パスが記録されるため、デバッグ情報は既定で削除されます。必要な場合は `-WithSymbols` を指定してください。nuget.org に接続できない環境では `-Source <folder>,<folder> -NoReadyToRun` でローカルキャッシュから復元できます。

## 特定チャネルでの起動

このフォークは特定のクライアント回線を追跡しており、upstream に対する修正を含みます:

- DLC アクティビティと時代進行。クライアント側が独自に時間判定するアクティビティ向けに、サーバーから送る Lua ペイロードも含みます
- Defend、Repression、Rogue3D のレベルクリア精算と難易度別クリア回数
- アクティビティ、クエスト、ショップにまたがるパケット対応の拡大

LAN / 直接ホストの協力ルームは断念しました。このブランチには含まれません。

そのチャネルのビルドでは通常のログインより多くの準備が要ります:

- **クライアントは 3.6.x**。SDK ハンドシェイクはチャンネル `pc_jinshan` / SDK `5.3.6` で応答します。そのチャネルの新しいビルドは、このフォークが持っていないパケット定義を必要とします。
- **起動時に `jinshan` チャンネル**。`Loader.Arguments` に `-ChannelID=jinshan` が入っています。これが無いとクライアントはそのチャネルの SDK エンドポイントを要求しません。
- **HTTPS は 7 ポート**。`HttpServer.Port`(21500) のほかに 443 / 11443 / 13443 / 18443 / 19443 / 31443 で待ち受けます。このチャネルの SDK は複数の固定エンドポイントへ別々のポートで接続するためです。443 が空いている必要があります。
- **TLS**。証明書はホスト名ごとの自己署名で `sdk-certs/` にキャッシュされます。インポートが拒否されると Kestrel が `Failed to start the SDK/HTTP (dispatch) server` を出し、ログインが完了しません。
- **ローカルプロキシか `hosts`**。`Proxy.Enabled` と `Loader.SetAllProxy`（既定でどちらも有効）なら SDK 通信は `127.0.0.1:18888` に向かい、上記のポートへ転送されます。プロキシを使わない場合はこれらのエンドポイントを `127.0.0.1` に向けてください。
- **リソース**。初回起動時に `Kei-Luna/MikuSB-Resource` を `Resources/` へダウンロードします。オフラインの場合は手動で配置してください。`item/templates/card.json` と `item/templates/weapon.json` が無いとデータ読み込みが止まります。
- **.NET 10 + ASP.NET Core ランタイム**。配布 zip は framework-dependent です。

ゲームサーバー本体は `127.0.0.1:21000`(TCP) を使用します。

## 機能一覧

* [x] ログインシステム
* [x] インベントリ
* [x] 戦闘
* [x] キャラクター
* [x] GMメニュー
* [x] 武器
* [x] 後方支援
* [x] アポカリプス
* [x] アウクトゥス
* [x] 神格神経
* [x] キャラスキン
* [x] 武器スキン
* [x] ガチャ
* [x] メインストーリーチャプター
* [x] 社員寮
* [x] ミニゲーム
* [x] 地下清掃
* [x] ニューロンシュミレーション
* [x] 戦術評価
* [x] エピソードストーリー
* [x] ルーチン作戦
* [x] 逆説迷宮
* [x] ロビー画面の編集
* [x] 惑星開拓
* [x] 夢の絵本
* [x] DLC アクティビティ / 時代進行
* [ ] マルチプレイ（LAN / 直接ホスト） — 断念
* [x] ショップ
* [x] 名誉紛争


## 貢献者  
- [Naruse](https://github.com/DevilProMT)
- [Kei-Luna](https://github.com/Kei-Luna)

## ライセンス
このリポジトリは [MIT License](LICENSE) の下で公開されています。

## 利用上の注意
本ソフトウェアはローカル環境での研究・検証用途を想定しています。  
公式サービスへの不正な接続、妨害、または商用利用を意図したものではありません。

## 法的免責事項
MikuSBは教育および研究目的で開発されました｡  
- 元のゲーム及び関連フランチャイズに関するすべての商標､著作権知的財産権はそれぞれの所有者に帰属します｡  
- このリポジトリには､著作権で保護されたゲームアセット､バイナリ､マスターデータは一切含まれていません｡  
- 自己責任でご利用下さい｡ 著者は､本ソフトウェアによって生じるいかなる損害または法的結果についても一切責任を負いません｡  

本ソフトウェアに関して懸念事項をお持ちの権利保有者は`devilpromt`または`kei_luna_re`にDiscordでご連絡下さい｡
