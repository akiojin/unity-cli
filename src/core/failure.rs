//! Typed failures shared by direct transport, daemon IPC and CLI presentation.
use super::editor_discovery::TargetError;
use anyhow::Error;
use serde_json::Value;

/// Stable process exit codes shared by CLI command implementations.
pub mod exit_code {
    pub const SUCCESS: i32 = 0;
    pub const GENERAL_ERROR: i32 = 1;
    pub const INVALID_ARGUMENT: i32 = 2;
    pub const UNAUTHORIZED: i32 = 3;
    pub const PRECONDITION_FAILED: i32 = 4;
    pub const OPERATION_FAILED: i32 = 6;
    pub const EDITOR_UNREACHABLE: i32 = 7;
    pub const TEST_FAILED: i32 = 8;
}

/// A Unity command failure, retaining the complete wire response for JSON output.
#[derive(Debug, thiserror::Error)]
#[error("{message}")]
pub struct UnityCommandError {
    pub response: Value,
    message: String,
}

impl UnityCommandError {
    pub fn new(response: Value) -> Self {
        let detail = response
            .as_array()
            .and_then(|items| items.iter().find(|item| item["ok"] == false))
            .unwrap_or(&response);
        let error = detail
            .get("error")
            .and_then(Value::as_str)
            .unwrap_or_else(|| {
                if detail
                    .get("status")
                    .and_then(Value::as_str)
                    .is_some_and(|status| status.eq_ignore_ascii_case("error"))
                {
                    "Unity command returned status=error"
                } else {
                    "Unity command failed"
                }
            });
        let code = detail
            .get("code")
            .and_then(Value::as_str)
            .unwrap_or("UNKNOWN_ERROR");
        let message = format!("{error} (code: {code})");
        Self { response, message }
    }
}

#[derive(Debug, thiserror::Error)]
pub enum FailureKind {
    #[error("INVALID_ARGUMENT")]
    Usage,
    #[error("EDITOR_UNREACHABLE")]
    Unreachable,
    #[error("OPERATION_FAILED")]
    Operation,
}

pub struct Failure {
    pub code: String,
    pub exit: i32,
    pub message: String,
    pub data: Value,
}

/// Map a machine-readable failure code to the stable process exit contract.
pub fn code_exit(code: &str) -> i32 {
    match code {
        "INVALID_ARGUMENT" => exit_code::INVALID_ARGUMENT,
        "UNAUTHORIZED" => exit_code::UNAUTHORIZED,
        "PRECONDITION_FAILED" | "TEST_RUNNER_DOMAIN_RELOAD_REQUIRED" | "VFX_API_UNSUPPORTED" => {
            exit_code::PRECONDITION_FAILED
        }
        "EDITOR_UNREACHABLE" | "EDITOR_NOT_FOUND" => exit_code::EDITOR_UNREACHABLE,
        "TEST_FAILED" => exit_code::TEST_FAILED,
        "GENERAL_ERROR" => exit_code::GENERAL_ERROR,
        _ => exit_code::OPERATION_FAILED,
    }
}

pub(crate) fn failed_tests(data: &Value) -> bool {
    data["status"] == "completed"
        && data.get("failedTests").is_some()
        && (data["success"] == false || data["failedTests"].as_u64().unwrap_or(0) > 0)
}

pub fn check_response(data: &Value) -> anyhow::Result<()> {
    if data["success"] == false
        || data["status"] == "error"
        || failed_tests(data)
        || data.get("error").is_some_and(|error| !error.is_null())
        || data
            .as_array()
            .is_some_and(|items| items.iter().any(|item| item["ok"] == false))
    {
        return Err(UnityCommandError::new(data.clone()).into());
    }
    Ok(())
}

pub fn classify(error: &Error) -> Failure {
    let (code, data) = if let Some(failure) = error.downcast_ref::<UnityCommandError>() {
        let data = failure.response.clone();
        let code = if failed_tests(&data) {
            "TEST_FAILED"
        } else {
            data["code"]
                .as_str()
                .or_else(|| data.pointer("/error/code").and_then(Value::as_str))
                .or_else(|| {
                    data.as_array()
                        .and_then(|items| items.iter().find(|item| item["ok"] == false))
                        .and_then(|item| item["code"].as_str())
                })
                .unwrap_or("OPERATION_FAILED")
        };
        (code.to_owned(), data)
    } else if let Some(target) = error.downcast_ref::<TargetError>() {
        (target.code().to_owned(), target.to_json()["data"].clone())
    } else if matches!(
        error.downcast_ref::<FailureKind>(),
        Some(FailureKind::Unreachable | FailureKind::Usage)
    ) {
        let kind = error.downcast_ref::<FailureKind>().unwrap();
        (kind.to_string(), Value::Null)
    } else if error
        .downcast_ref::<tokio::time::error::Elapsed>()
        .is_some()
    {
        ("TIMEOUT".to_owned(), Value::Null)
    } else if let Some(kind) = error.downcast_ref::<FailureKind>() {
        (kind.to_string(), Value::Null)
    } else {
        ("GENERAL_ERROR".to_owned(), Value::Null)
    };
    let exit = error
        .downcast_ref::<TargetError>()
        .map_or_else(|| code_exit(&code), TargetError::exit_code);
    Failure {
        exit,
        code,
        message: format!("{error:#}"),
        data,
    }
}

pub fn batch_error(error: &Error) -> Value {
    let failure = classify(error);
    serde_json::json!({"ok":false, "code":failure.code, "error":failure.message, "details":failure.data})
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn failed_batch_preserves_first_failure_message_and_all_results() {
        let response = serde_json::json!([
            {"ok": true, "result": {"message": "pong"}},
            {"ok": false, "code": "EDITOR_UNREACHABLE", "error": "Failed to connect to Unity at 127.0.0.1:9"},
            {"ok": false, "code": "UNAUTHORIZED", "error": "Missing credential"}
        ]);
        let error = check_response(&response).unwrap_err();
        assert!(format!("{error:#}").contains("Failed to connect to Unity at 127.0.0.1:9"));
        let failure = classify(&error);
        assert_eq!(failure.code, "EDITOR_UNREACHABLE");
        assert_eq!(failure.exit, exit_code::EDITOR_UNREACHABLE);
        assert_eq!(failure.data, response);
    }
}
