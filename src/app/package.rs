use std::time::{Duration, SystemTime, UNIX_EPOCH};

use anyhow::Result;
use serde_json::{json, Value};
use tokio::time::{sleep, timeout_at, Instant};

use crate::config::RuntimeConfig;
use crate::transport::{UnityClient, UnityCommandError};

pub(crate) fn is_import(params: &Value) -> bool {
    matches!(params["action"].as_str(), Some("import" | "import_cached"))
}

pub(crate) fn apply_cache_override(params: &mut Value) {
    if matches!(
        params["action"].as_str(),
        Some("list_cached" | "import_cached")
    ) && params.get("cachePath").is_none()
    {
        if let Ok(path) = std::env::var("UNITY_CLI_ASSET_STORE_CACHE_PATH") {
            if !path.trim().is_empty() {
                params["cachePath"] = json!(path);
            }
        }
    }
}

/// Submit once, then reconnect only to read the saved request. Importing scripts
/// can reload the domain and drop even the initial response.
pub(crate) async fn import_and_wait(config: &RuntimeConfig, mut params: Value) -> Result<Value> {
    let timeout_ms = params["timeoutMs"].as_u64().unwrap_or(180_000);
    if timeout_ms == 0
        || timeout_ms > 86_400_000
        || (params.get("timeoutMs").is_some() && params["timeoutMs"].as_u64().is_none())
        || params["interactive"] == true
    {
        return Err(UnityCommandError::new(json!({
            "success":false, "code":"INVALID_ARGUMENT",
            "error":"timeoutMs must be between 1 and 86400000, and interactive must be false"
        }))
        .into());
    }
    let deadline = Instant::now() + Duration::from_millis(timeout_ms);
    let request_id = params["requestId"]
        .as_str()
        .map(str::to_owned)
        .unwrap_or_else(|| {
            static NEXT_ID: std::sync::atomic::AtomicU64 = std::sync::atomic::AtomicU64::new(0);
            let timestamp = SystemTime::now()
                .duration_since(UNIX_EPOCH)
                .unwrap_or_default()
                .as_nanos();
            let sequence = NEXT_ID.fetch_add(1, std::sync::atomic::Ordering::Relaxed);
            format!("package-{}-{timestamp:x}-{sequence:x}", std::process::id())
        });
    params["requestId"] = json!(request_id);
    params["interactive"] = json!(false);
    let mut state = json!({"requestId":request_id, "status":"unknown", "compileState":null});
    let mut last_transport_error = Value::Null;
    let mut first = true;
    loop {
        let is_submission = first;
        let request = if first {
            first = false;
            params.clone()
        } else {
            json!({"action":"import_status", "requestId":request_id})
        };
        let call = async {
            let mut client = UnityClient::connect(config).await?;
            client.call_tool("package_manager", request).await
        };
        match timeout_at(deadline, call).await {
            Ok(Ok(value)) => {
                if value["status"] == "completed" {
                    return Ok(value);
                }
                state = value;
            }
            Ok(Err(error))
                if !is_submission
                    && error
                        .downcast_ref::<UnityCommandError>()
                        .is_some_and(|failure| failure.response["code"] == "UNAUTHORIZED") =>
            {
                // Reload rotates the token before the Editor rewrites its lockfile.
                // The next read reconnects and reloads that same credential file;
                // an unauthorized mutation is never resubmitted.
                last_transport_error = json!(format!("{error:#}"));
            }
            Ok(Err(error)) if error.downcast_ref::<UnityCommandError>().is_some() => {
                return Err(error);
            }
            Ok(Err(error)) => last_transport_error = json!(format!("{error:#}")),
            Err(_) => break,
        }
        if timeout_at(deadline, sleep(Duration::from_millis(250)))
            .await
            .is_err()
        {
            break;
        }
    }
    state["success"] = json!(false);
    state["code"] = json!("PACKAGE_IMPORT_TIMEOUT");
    state["error"] = json!("Timed out waiting for package import, compilation and domain reload; query package import-status with requestId before retrying");
    state["requestId"] = json!(request_id);
    state["lastTransportError"] = last_transport_error;
    Err(UnityCommandError::new(state).into())
}

#[cfg(test)]
#[allow(clippy::await_holding_lock)] // Environment isolation spans the async mock requests.
mod tests {
    use super::*;
    use tokio::io::{AsyncReadExt, AsyncWriteExt};
    use tokio::net::TcpListener;

    #[test]
    fn package_cache_override_preserves_explicit_path_and_ignores_unrelated_actions() {
        let lock = crate::test_env::env_lock()
            .lock()
            .unwrap_or_else(|p| p.into_inner());
        let _env = crate::test_env::TestEnvironment::new(&lock);
        std::env::set_var("UNITY_CLI_ASSET_STORE_CACHE_PATH", "C:/custom-cache");
        let mut cached = json!({"action":"list_cached"});
        apply_cache_override(&mut cached);
        assert_eq!(cached["cachePath"], "C:/custom-cache");
        cached["cachePath"] = json!("C:/explicit");
        apply_cache_override(&mut cached);
        assert_eq!(cached["cachePath"], "C:/explicit");
        let mut unrelated = json!({"action":"list"});
        apply_cache_override(&mut unrelated);
        assert!(unrelated.get("cachePath").is_none());
    }

    async fn server(
        responses: Vec<Option<Value>>,
    ) -> (RuntimeConfig, tokio::task::JoinHandle<Vec<Value>>) {
        let listener = TcpListener::bind("127.0.0.1:0").await.unwrap();
        let port = listener.local_addr().unwrap().port();
        let handle = tokio::spawn(async move {
            let mut requests = Vec::new();
            for response in responses {
                let (mut socket, _) = listener.accept().await.unwrap();
                let size = socket.read_u32().await.unwrap();
                let mut bytes = vec![0; size as usize];
                socket.read_exact(&mut bytes).await.unwrap();
                requests.push(serde_json::from_slice::<Value>(&bytes).unwrap()["params"].clone());
                if let Some(value) = response {
                    let payload =
                        serde_json::to_vec(&json!({"status":"success", "result":value})).unwrap();
                    socket.write_u32(payload.len() as u32).await.unwrap();
                    socket.write_all(&payload).await.unwrap();
                }
            }
            requests
        });
        (
            RuntimeConfig {
                host: "127.0.0.1".into(),
                port,
                timeout: Duration::from_secs(1),
            },
            handle,
        )
    }

    #[tokio::test]
    async fn package_import_reconnects_after_reload_without_repeating_mutation() {
        let lock = crate::test_env::env_lock()
            .lock()
            .unwrap_or_else(|p| p.into_inner());
        let _env = crate::test_env::TestEnvironment::new(&lock);
        let (config, handle) = server(vec![
            None,
            Some(
                json!({"success":true, "status":"importing", "compileState":{"isCompiling":true}}),
            ),
            Some(
                json!({"success":true, "status":"completed", "importedAssets":["Assets/probe.cs"]}),
            ),
        ])
        .await;
        let result = import_and_wait(
            &config,
            json!({"action":"import", "path":"small.unitypackage", "timeoutMs":3000}),
        )
        .await
        .unwrap();
        assert_eq!(result["importedAssets"], json!(["Assets/probe.cs"]));
        let requests = handle.await.unwrap();
        assert_eq!(requests[0]["action"], "import");
        assert_eq!(requests[0]["interactive"], false);
        for request in &requests[1..] {
            assert_eq!(request["action"], "import_status");
            assert_eq!(request["requestId"], requests[0]["requestId"]);
        }
    }

    #[tokio::test]
    async fn package_import_reloads_credentials_only_for_status_reads() {
        let lock = crate::test_env::env_lock()
            .lock()
            .unwrap_or_else(|p| p.into_inner());
        let _env = crate::test_env::TestEnvironment::new(&lock);
        let (config, handle) = server(vec![
            Some(json!({"success":true, "status":"importing"})),
            Some(json!({"success":false, "code":"UNAUTHORIZED", "error":"token refreshing"})),
            Some(json!({"success":true, "status":"completed"})),
        ])
        .await;
        let result = import_and_wait(
            &config,
            json!({"action":"import", "path":"scripts.unitypackage", "timeoutMs":3000}),
        )
        .await
        .unwrap();
        assert_eq!(result["status"], "completed");
        let requests = handle.await.unwrap();
        assert_eq!(
            requests
                .iter()
                .filter(|request| request["action"] == "import")
                .count(),
            1
        );
        assert_eq!(requests[1]["action"], "import_status");
        assert_eq!(requests[2]["action"], "import_status");
    }

    #[tokio::test]
    async fn package_import_timeout_preserves_request_and_current_compilation_state() {
        let lock = crate::test_env::env_lock()
            .lock()
            .unwrap_or_else(|p| p.into_inner());
        let _env = crate::test_env::TestEnvironment::new(&lock);
        let (config, handle) = server(vec![Some(json!({"success":true, "status":"imported",
            "compileState":{"isCompiling":true, "isUpdating":false}}))])
        .await;
        let error = import_and_wait(
            &config,
            json!({"action":"import_cached", "nameOrId":"Small",
            "requestId":"timeout-test", "timeoutMs":50}),
        )
        .await
        .unwrap_err();
        let response = &error.downcast_ref::<UnityCommandError>().unwrap().response;
        assert_eq!(response["code"], "PACKAGE_IMPORT_TIMEOUT");
        assert_eq!(response["requestId"], "timeout-test");
        assert_eq!(response["status"], "imported");
        assert_eq!(response["compileState"]["isCompiling"], true);
        assert_eq!(handle.await.unwrap().len(), 1);
    }

    #[tokio::test]
    async fn package_import_command_failure_is_not_retried() {
        let lock = crate::test_env::env_lock()
            .lock()
            .unwrap_or_else(|p| p.into_inner());
        let _env = crate::test_env::TestEnvironment::new(&lock);
        for code in ["INVALID_UNITYPACKAGE", "UNAUTHORIZED"] {
            let (config, handle) = server(vec![Some(
                json!({"success":false, "code":code, "error":"rejected"}),
            )])
            .await;
            let error = import_and_wait(
                &config,
                json!({"action":"import", "path":"broken.unitypackage"}),
            )
            .await
            .unwrap_err();
            assert_eq!(
                error.downcast_ref::<UnityCommandError>().unwrap().response["code"],
                code
            );
            assert_eq!(handle.await.unwrap().len(), 1);
        }
    }
}
