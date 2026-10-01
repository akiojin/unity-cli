//! Public CLI result contract. Bridge payloads remain intact under `data`.
use anyhow::{Error, Result};
use serde_json::{json, Value};

use crate::cli::OutputFormat;

use crate::core::failure::{check_response, Failure};
pub use crate::core::failure::{classify, FailureKind};

pub fn envelope(command: &str, data: &Value, failure: Option<&Failure>) -> Value {
    let errors = failure.map_or_else(
        || json!([]),
        |f| json!([{ "code": f.code, "message": f.message }]),
    );
    json!({
        "success": failure.is_none(), "command": command, "data": data,
        "errors": errors,
        "warnings": data.get("warnings").filter(|v| v.is_array()).cloned().unwrap_or_else(|| json!([]))
    })
}

pub fn print_failure(command: &str, error: &Error, format: OutputFormat) -> Result<()> {
    let failure = classify(error);
    if matches!(format, OutputFormat::Json) {
        println!(
            "{}",
            serde_json::to_string(&envelope(command, &failure.data, Some(&failure)))?
        );
    } else {
        eprintln!("{}: {}", failure.code, failure.message);
        if let Some(candidates) = failure.data["candidates"].as_array() {
            for candidate in candidates {
                eprintln!("  candidate: {candidate}");
            }
        }
    }
    Ok(())
}

pub fn print_result(command: &str, data: &Value, format: OutputFormat) -> Result<()> {
    check_response(data)?;
    match format {
        OutputFormat::Json => {
            println!("{}", serde_json::to_string(&envelope(command, data, None))?)
        }
        OutputFormat::Text => match data.as_str() {
            Some(text) => println!("{text}"),
            None => println!("{}", serde_json::to_string_pretty(data)?),
        },
    }
    Ok(())
}
