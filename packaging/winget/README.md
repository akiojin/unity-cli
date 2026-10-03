# winget 初回公開用 manifest

`akiojin.unity-cli/0.18.1/` は公開 Release v0.18.1 の Windows x64 portable
バイナリを対象にした初回提出用の3ファイル manifest です。
InstallerSha256 は GitHub Release の asset digest と、インストーラで検証済みの
バイナリの SHA256 に一致します。

```powershell
winget validate --manifest packaging/winget/akiojin.unity-cli/0.18.1
```

提出先は microsoft/winget-pkgs の
`manifests/a/akiojin/unity-cli/0.18.1/` です。公開 source への登録はまだ完了していません。
[Microsoft の提出手順](https://learn.microsoft.com/en-us/windows/package-manager/package/repository)
に沿って、この3ファイルだけを初回 package PR に含めます。

後続リリースの自動更新には repository secret `WINGET_TOKEN` が必要です。
既存の release workflow の `update-winget` job がこの secret を使います。
[Microsoft の認証手順](https://github.com/microsoft/winget-create/blob/main/doc/token.md)
を参照し、公開リポジトリへ提出できる資格情報を設定します。トークン値は証跡や
コマンドライン引数に残さないでください。

公開 source への反映後に、次を実 Windows と開いた検証用 Editor で確認します。

```powershell
winget install --id akiojin.unity-cli --exact --source winget --scope user
unity-cli --version
unity-cli --port 6487 system ping
```

ローカル manifest の validate 成功は、公開 source からの install 成功とは別の証跡です。
