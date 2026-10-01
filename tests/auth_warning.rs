use std::process::Command;

#[test]
fn legacy_opt_out_warns_on_stderr_only_for_exact_one() {
    for (value, expected) in [
        (None, false),
        (Some("0"), false),
        (Some("true"), false),
        (Some("1"), true),
    ] {
        let mut command = Command::new(env!("CARGO_BIN_EXE_unity-cli"));
        command
            .args(["tool", "list", "--output", "json"])
            .env("UNITY_CLI_NO_AUTO_UPDATE", "1")
            .env_remove("UNITY_CLI_ALLOW_UNAUTHENTICATED");
        if let Some(value) = value {
            command.env("UNITY_CLI_ALLOW_UNAUTHENTICATED", value);
        }
        let output = command.output().unwrap();
        assert!(output.status.success());
        let stderr = String::from_utf8(output.stderr).unwrap();
        assert_eq!(stderr.contains("next minor release"), expected, "{stderr}");
        serde_json::from_slice::<serde_json::Value>(&output.stdout).unwrap();
    }
}
