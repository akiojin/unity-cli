use std::time::{Duration, SystemTime, UNIX_EPOCH};

use anyhow::Result;
use serde_json::{json, Value};
use tokio::time::{sleep, timeout_at, Instant};

use crate::config::RuntimeConfig;
use crate::transport::{UnityClient, UnityCommandError};

pub(crate) fn is_import(params: &Value) -> bool {
    matches!(params["action"].as_str(), Some("import" | "import_cached"))
}

pub(crate) fn is_download_flow(params: &Value) -> bool {
    matches!(params["action"].as_str(), Some("wait_download" | "acquire"))
}

pub(crate) fn apply_cache_override(params: &mut Value) {
    if matches!(
        params["action"].as_str(),
        Some("list_cached" | "import_cached" | "open_asset_store" | "wait_download" | "acquire")
    ) && params.get("cachePath").is_none()
    {
        if let Ok(path) = std::env::var("UNITY_CLI_ASSET_STORE_CACHE_PATH") {
            if !path.trim().is_empty() {
                params["cachePath"] = json!(path);
            }
        }
    }
}

/// Only display is a mutation before import. Polls read the Editor's cache, so
/// the CLI can run on a different machine and never needs Unity credentials.
pub(crate) async fn download_and_wait(config: &RuntimeConfig, mut params: Value) -> Result<Value> {
    let duration = params["downloadTimeoutMs"].as_u64().unwrap_or(600_000);
    let acquire = params["action"] == "acquire";
    let import_duration = params["timeoutMs"].as_u64().unwrap_or(180_000);
    if duration == 0
        || duration > 86_400_000
        || (params.get("downloadTimeoutMs").is_some()
            && params["downloadTimeoutMs"].as_u64().is_none())
        || params["nameOrId"]
            .as_str()
            .is_none_or(|s| s.trim().is_empty())
        || (acquire
            && (import_duration == 0
                || import_duration > 86_400_000
                || (params.get("timeoutMs").is_some() && params["timeoutMs"].as_u64().is_none())
                || params["interactive"] == true))
    {
        return Err(UnityCommandError::new(json!({"success":false,"code":"INVALID_ARGUMENT",
            "error":"nameOrId is required; timeouts must be integers between 1 and 86400000; interactive must be false"})).into());
    }
    let opened = if acquire {
        let mut open = params.clone();
        open["action"] = json!("open_asset_store");
        let mut client = UnityClient::connect(config).await?;
        let result = client.call_tool("package_manager", open).await?;
        eprintln!("My Assets opened. Select the requested asset if needed and press Download manually. Waiting for the cache...");
        result
    } else {
        Value::Null
    };
    let deadline = Instant::now() + Duration::from_millis(duration);
    params["action"] = json!("wait_download");
    let mut state = json!({"status":"waiting_download"});
    let mut last_transport_error = Value::Null;
    loop {
        let call = async {
            let mut client = UnityClient::connect(config).await?;
            client.call_tool("package_manager", params.clone()).await
        };
        match timeout_at(deadline, call).await {
            Ok(Ok(value)) if value["status"] == "ready" => {
                if !acquire {
                    return Ok(value);
                }
                let path = value["path"]
                    .as_str()
                    .filter(|s| !s.is_empty())
                    .ok_or_else(|| {
                        UnityCommandError::new(json!({"success":false,"code":"INVALID_RESPONSE",
                        "error":"Download probe returned ready without a package path"}))
                    })?;
                let mut result = import_and_wait(
                    config,
                    json!({"action":"import","path":path,
                    "timeoutMs":import_duration,"requestId":params["requestId"]}),
                )
                .await?;
                result["assetStore"] = opened;
                return Ok(result);
            }
            Ok(Ok(value)) => state = value,
            Ok(Err(error)) if error.downcast_ref::<UnityCommandError>().is_some() => {
                return Err(error)
            }
            Ok(Err(error)) => last_transport_error = json!(format!("{error:#}")),
            Err(_) => break,
        }
        if timeout_at(deadline, sleep(Duration::from_millis(500)))
            .await
            .is_err()
        {
            break;
        }
    }
    state["success"] = json!(false);
    state["status"] = json!("waiting_download");
    state["code"] = json!("ASSET_STORE_DOWNLOAD_TIMEOUT");
    state["error"] = json!("Timed out waiting for a complete cached .unitypackage. Download must be pressed manually in My Assets; no import was started.");
    state["nameOrId"] = params["nameOrId"].clone();
    state["assetStore"] = opened;
    state["lastTransportError"] = last_transport_error;
    Err(UnityCommandError::new(state).into())
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
        for action in ["wait_download", "acquire", "open_asset_store"] {
            let mut request = json!({"action":action});
            apply_cache_override(&mut request);
            assert_eq!(request["cachePath"], "C:/custom-cache");
        }
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
    async fn asset_store_flow_opens_once_waits_then_reuses_import_and_status() {
        let lock = crate::test_env::env_lock()
            .lock()
            .unwrap_or_else(|p| p.into_inner());
        let _env = crate::test_env::TestEnvironment::new(&lock);
        let (config, handle) = server(vec![
            Some(
                json!({"success":true,"status":"awaiting_manual_download","selection":"requested"}),
            ),
            Some(json!({"success":true,"status":"waiting_download"})),
            Some(json!({"success":true,"status":"ready","path":"C:/cache/Small.unitypackage"})),
            Some(json!({"success":true,"status":"importing"})),
            Some(
                json!({"success":true,"status":"completed","importedAssets":["Assets/Small.txt"]}),
            ),
        ])
        .await;
        let result = download_and_wait(
            &config,
            json!({"action":"acquire","nameOrId":"12345",
            "cachePath":"C:/cache","downloadTimeoutMs":3000,"timeoutMs":3000}),
        )
        .await
        .unwrap();
        assert_eq!(result["importedAssets"], json!(["Assets/Small.txt"]));
        assert_eq!(result["assetStore"]["selection"], "requested");
        let requests = handle.await.unwrap();
        assert_eq!(
            requests
                .iter()
                .map(|r| r["action"].as_str().unwrap())
                .collect::<Vec<_>>(),
            [
                "open_asset_store",
                "wait_download",
                "wait_download",
                "import",
                "import_status"
            ]
        );
        assert_eq!(requests[1]["cachePath"], "C:/cache");
        assert_eq!(requests[3]["path"], "C:/cache/Small.unitypackage");
        assert_eq!(requests[3]["interactive"], false);
    }

    #[tokio::test]
    async fn asset_store_wait_alone_never_opens_or_imports() {
        let lock = crate::test_env::env_lock()
            .lock()
            .unwrap_or_else(|p| p.into_inner());
        let _env = crate::test_env::TestEnvironment::new(&lock);
        let (config, handle) = server(vec![Some(
            json!({"success":true,"status":"ready","path":"Small.unitypackage"}),
        )])
        .await;
        let result = download_and_wait(
            &config,
            json!({"action":"wait_download","nameOrId":"Small"}),
        )
        .await
        .unwrap();
        assert_eq!(result["status"], "ready");
        assert_eq!(handle.await.unwrap().len(), 1);
    }

    #[tokio::test]
    async fn asset_store_wait_reconnects_after_read_disconnect() {
        let lock = crate::test_env::env_lock()
            .lock()
            .unwrap_or_else(|p| p.into_inner());
        let _env = crate::test_env::TestEnvironment::new(&lock);
        let (config, handle) = server(vec![
            None,
            Some(json!({"success":true,"status":"ready","path":"Small.unitypackage"})),
        ])
        .await;
        assert_eq!(
            download_and_wait(
                &config,
                json!({"action":"wait_download","nameOrId":"Small",
            "downloadTimeoutMs":3000})
            )
            .await
            .unwrap()["status"],
            "ready"
        );
        assert!(handle
            .await
            .unwrap()
            .iter()
            .all(|request| request["action"] == "wait_download"));
    }

    #[tokio::test]
    async fn asset_store_open_failure_is_not_retried_and_never_imports() {
        let lock = crate::test_env::env_lock()
            .lock()
            .unwrap_or_else(|p| p.into_inner());
        let _env = crate::test_env::TestEnvironment::new(&lock);
        let (config, handle) = server(vec![Some(
            json!({"success":false,"code":"ASSET_STORE_UI_UNAVAILABLE",
            "error":"A headed Editor is required"}),
        )])
        .await;
        let error = download_and_wait(&config, json!({"action":"acquire","nameOrId":"12345"}))
            .await
            .unwrap_err();
        assert_eq!(
            error.downcast_ref::<UnityCommandError>().unwrap().response["code"],
            "ASSET_STORE_UI_UNAVAILABLE"
        );
        assert_eq!(handle.await.unwrap().len(), 1);
    }

    #[tokio::test]
    async fn asset_store_rejects_missing_path_before_submitting_import() {
        let lock = crate::test_env::env_lock()
            .lock()
            .unwrap_or_else(|p| p.into_inner());
        let _env = crate::test_env::TestEnvironment::new(&lock);
        let (config, handle) = server(vec![
            Some(json!({"success":true,"status":"awaiting_manual_download"})),
            Some(json!({"success":true,"status":"ready"})),
        ])
        .await;
        let error = download_and_wait(&config, json!({"action":"acquire","nameOrId":"12345"}))
            .await
            .unwrap_err();
        assert_eq!(
            error.downcast_ref::<UnityCommandError>().unwrap().response["code"],
            "INVALID_RESPONSE"
        );
        assert_eq!(handle.await.unwrap().len(), 2);
    }

    #[tokio::test]
    async fn asset_store_timeout_reports_manual_download_wait_without_import() {
        let lock = crate::test_env::env_lock()
            .lock()
            .unwrap_or_else(|p| p.into_inner());
        let _env = crate::test_env::TestEnvironment::new(&lock);
        let (config, handle) = server(vec![Some(
            json!({"success":true,"status":"waiting_download","reason":"incomplete_archive"}),
        )])
        .await;
        let error = download_and_wait(
            &config,
            json!({"action":"wait_download","nameOrId":"Small","downloadTimeoutMs":50}),
        )
        .await
        .unwrap_err();
        let response = &error.downcast_ref::<UnityCommandError>().unwrap().response;
        assert_eq!(response["code"], "ASSET_STORE_DOWNLOAD_TIMEOUT");
        assert_eq!(response["status"], "waiting_download");
        assert_eq!(response["nameOrId"], "Small");
        assert_eq!(response["reason"], "incomplete_archive");
        assert_eq!(handle.await.unwrap().len(), 1);
    }

    #[tokio::test]
    async fn asset_store_rejects_ambiguous_package_and_invalid_deadline() {
        let lock = crate::test_env::env_lock()
            .lock()
            .unwrap_or_else(|p| p.into_inner());
        let _env = crate::test_env::TestEnvironment::new(&lock);
        let (config, handle) = server(vec![Some(
            json!({"success":false,"code":"PACKAGE_AMBIGUOUS","error":"Multiple matches"}),
        )])
        .await;
        let error = download_and_wait(
            &config,
            json!({"action":"wait_download","nameOrId":"Small"}),
        )
        .await
        .unwrap_err();
        assert_eq!(
            error.downcast_ref::<UnityCommandError>().unwrap().response["code"],
            "PACKAGE_AMBIGUOUS"
        );
        assert_eq!(handle.await.unwrap().len(), 1);
        for value in [json!(0), json!(-1), json!(86400001), json!("1000")] {
            let error = download_and_wait(
                &config,
                json!({"action":"acquire","nameOrId":"Small","downloadTimeoutMs":value}),
            )
            .await
            .unwrap_err();
            assert_eq!(
                error.downcast_ref::<UnityCommandError>().unwrap().response["code"],
                "INVALID_ARGUMENT"
            );
            let error = download_and_wait(
                &config,
                json!({"action":"acquire","nameOrId":"Small","timeoutMs":value}),
            )
            .await
            .unwrap_err();
            assert_eq!(
                error.downcast_ref::<UnityCommandError>().unwrap().response["code"],
                "INVALID_ARGUMENT"
            );
        }
        assert!(
            download_and_wait(&config, json!({"action":"wait_download","nameOrId":" "}))
                .await
                .is_err()
        );
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
