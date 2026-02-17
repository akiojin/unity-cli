# Attribution Guide / 帰属表示ガイド

This document provides templates and guidance for including MIT license attribution for `unity-cli` and `unity-cli-bridge` in your projects.

本ドキュメントは、`unity-cli` および `unity-cli-bridge` の MIT ライセンス帰属表示をプロジェクトに含めるためのテンプレートとガイダンスを提供します。

---

## English

### MIT License Notice

```
MIT License

Copyright (c) akiojin

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

### Example Attribution Text

You can use the following text in your application's credits, about screen, or documentation:

```
This product includes software developed by akiojin.
unity-cli - https://github.com/akiojin/unity-cli
Licensed under the MIT License.
```

### Example NOTICE File Content

If your project uses a `NOTICE` or `ThirdPartyNotices.txt` file, add the following entry:

```
unity-cli
https://github.com/akiojin/unity-cli
Copyright (c) akiojin
MIT License

unity-cli-bridge (UPM package: com.akiojin.unity-cli-bridge)
https://github.com/akiojin/unity-cli.git?path=UnityCliBridge/Packages/unity-cli-bridge
Copyright (c) akiojin
MIT License
```

### Including Attribution in Unity Builds

When shipping a Unity application that includes the `unity-cli-bridge` UPM package, the MIT license requires you to include the copyright notice and permission notice. Here are common approaches:

1. **TextAsset in Resources**: Place a `NOTICES.txt` file in `Assets/Resources/` and display it in a credits or legal screen.

2. **StreamingAssets**: Place the notice file in `Assets/StreamingAssets/` so it is included in the build output as-is.

3. **About/Credits Screen**: Include the attribution text in your application's credits or about screen UI.

4. **Documentation**: Include the notice in your application's user manual or online documentation.

Since `unity-cli-bridge` is an Editor-only package (it runs only in the Unity Editor, not in builds), attribution in shipped builds may not be strictly required. However, if your project also redistributes or modifies the source code, attribution is necessary.

---

## 日本語

### MIT ライセンス表記

```
MIT License

Copyright (c) akiojin

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

### 帰属表示の記載例

アプリケーションのクレジット画面やドキュメントに以下のテキストを使用できます。

```
本製品には akiojin が開発したソフトウェアが含まれています。
unity-cli - https://github.com/akiojin/unity-cli
MIT License に基づきライセンスされています。
```

### NOTICE ファイルの記載例

プロジェクトで `NOTICE` や `ThirdPartyNotices.txt` を管理している場合は、以下のエントリを追加してください。

```
unity-cli
https://github.com/akiojin/unity-cli
Copyright (c) akiojin
MIT License

unity-cli-bridge (UPM パッケージ: com.akiojin.unity-cli-bridge)
https://github.com/akiojin/unity-cli.git?path=UnityCliBridge/Packages/unity-cli-bridge
Copyright (c) akiojin
MIT License
```

### Unity ビルドへの帰属表示の含め方

`unity-cli-bridge` UPM パッケージを含む Unity アプリケーションを配布する場合、MIT ライセンスでは著作権表示と許諾表示の同梱が求められます。一般的な方法を以下に示します。

1. **Resources 内の TextAsset**: `Assets/Resources/` に `NOTICES.txt` を配置し、クレジット画面や法的情報画面で表示します。

2. **StreamingAssets**: `Assets/StreamingAssets/` にファイルを配置すると、ビルド出力にそのまま含まれます。

3. **クレジット画面**: アプリケーションのクレジットまたは「About」画面の UI に帰属表示テキストを組み込みます。

4. **ドキュメント**: アプリケーションのユーザーマニュアルやオンラインドキュメントに表記します。

`unity-cli-bridge` は Editor 専用パッケージ（Unity Editor 内でのみ動作し、ビルドには含まれない）であるため、出荷ビルドでの帰属表示は厳密には不要な場合があります。ただし、ソースコードを再配布または改変する場合は帰属表示が必要です。
