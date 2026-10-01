//! Portable test report rendering.
use serde_json::{json, Value};

fn xml(text: &str) -> String {
    text.chars()
        .filter(|c| {
            matches!(c, '\t' | '\n' | '\r') || (*c >= ' ' && *c != '\u{fffe}' && *c != '\u{ffff}')
        })
        .collect::<String>()
        .replace('&', "&amp;")
        .replace('<', "&lt;")
        .replace('>', "&gt;")
        .replace('"', "&quot;")
        .replace('\'', "&apos;")
}

fn text<'a>(value: &'a Value, field: &str) -> &'a str {
    value[field].as_str().unwrap_or("")
}

fn failures(result: &Value) -> Vec<&Value> {
    let all: Vec<_> = result["failures"]
        .as_array()
        .into_iter()
        .flatten()
        .collect();
    // UTF emits this diagnostic for every ancestor of a failed test. It is
    // a rollup, not an additional failed test or a setup/teardown error.
    let meaningful: Vec<_> = all
        .iter()
        .copied()
        .filter(|failure| text(failure, "message") != "One or more child tests had errors")
        .collect();
    if meaningful.is_empty() && result["success"] == false {
        all.into_iter().take(1).collect()
    } else {
        meaningful
    }
}

fn cases(result: &Value) -> Vec<Value> {
    let mut tests = result["tests"].as_array().cloned().unwrap_or_default();
    for failure in failures(result) {
        if !tests.iter().any(|t| t["fullName"] == failure["testName"]) {
            tests.push(json!({"name":failure["testName"],"fullName":failure["testName"],"status":"Failed","message":failure["message"],"duration":0}));
        }
    }
    tests
}

pub fn junit(result: &Value) -> String {
    let tests = cases(result);
    let failed = tests.iter().filter(|t| t["status"] == "Failed").count();
    let skipped = tests
        .iter()
        .filter(|t| t["status"] == "Skipped" || t["status"] == "Inconclusive")
        .count();
    let duration: f64 = tests
        .iter()
        .map(|t| t["duration"].as_f64().unwrap_or(0.0))
        .sum();
    let mut report = format!("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<testsuites><testsuite name=\"Unity\" tests=\"{}\" failures=\"{failed}\" errors=\"0\" skipped=\"{skipped}\" time=\"{duration:.3}\">", tests.len());
    for test in tests {
        let classname = text(&test, "fullName")
            .rsplit_once('.')
            .map_or("Unity", |p| p.0);
        report.push_str(&format!(
            "<testcase classname=\"{}\" name=\"{}\" time=\"{:.3}\">",
            xml(classname),
            xml(text(&test, "name")),
            test["duration"].as_f64().unwrap_or(0.0)
        ));
        match text(&test, "status") {
            "Failed" => report.push_str(&format!(
                "<failure message=\"{}\">{}</failure>",
                xml(text(&test, "message")),
                xml(text(&test, "message"))
            )),
            "Skipped" | "Inconclusive" => report.push_str("<skipped/>"),
            _ => (),
        }
        if !text(&test, "output").is_empty() {
            report.push_str(&format!(
                "<system-out>{}</system-out>",
                xml(text(&test, "output"))
            ));
        }
        report.push_str("</testcase>");
    }
    report.push_str("</testsuite></testsuites>\n");
    report
}

pub fn nunit(result: &Value) -> String {
    let tests = cases(result);
    let failed = tests.iter().filter(|t| t["status"] == "Failed").count();
    let passed = tests.iter().filter(|t| t["status"] == "Passed").count();
    let inconclusive = tests
        .iter()
        .filter(|t| t["status"] == "Inconclusive")
        .count();
    let skipped = tests.len() - failed - passed - inconclusive;
    let status = if result["success"] == false || failed > 0 {
        "Failed"
    } else {
        "Passed"
    };
    let mut report = format!("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<test-run id=\"0\" name=\"Unity\" testcasecount=\"{}\" total=\"{}\" result=\"{status}\" passed=\"{passed}\" failed=\"{failed}\" skipped=\"{skipped}\" inconclusive=\"{inconclusive}\">", tests.len(), tests.len());
    report.push_str(&format!("<test-suite id=\"0-suite\" type=\"TestSuite\" name=\"Unity\" fullname=\"Unity\" result=\"{status}\" testcasecount=\"{}\" total=\"{}\" passed=\"{passed}\" failed=\"{failed}\" skipped=\"{skipped}\" inconclusive=\"{inconclusive}\">",tests.len(),tests.len()));
    for (id, test) in tests.iter().enumerate() {
        report.push_str(&format!(
            "<test-case id=\"0-{}\" name=\"{}\" fullname=\"{}\" result=\"{}\" duration=\"{}\">",
            id + 1,
            xml(text(test, "name")),
            xml(text(test, "fullName")),
            xml(text(test, "status")),
            test["duration"].as_f64().unwrap_or(0.0)
        ));
        if test["status"] == "Failed" {
            let stack = result["failures"]
                .as_array()
                .into_iter()
                .flatten()
                .find(|f| f["testName"] == test["fullName"])
                .map_or("", |f| text(f, "stackTrace"));
            report.push_str(&format!(
                "<failure><message>{}</message><stack-trace>{}</stack-trace></failure>",
                xml(text(test, "message")),
                xml(stack)
            ));
        }
        if !text(test, "output").is_empty() {
            report.push_str(&format!("<output>{}</output>", xml(text(test, "output"))));
        }
        report.push_str("</test-case>");
    }
    report.push_str("</test-suite></test-run>\n");
    report
}

fn escape_command(text: &str) -> String {
    text.replace('%', "%25")
        .replace('\r', "%0D")
        .replace('\n', "%0A")
}

pub fn annotations(result: &Value) -> Vec<String> {
    let location = regex::Regex::new(r"(?:\(at | in )(.+?):(?:line )?(\d+)\)?(?:\r?\n|$)").unwrap();
    failures(result)
        .into_iter()
        .map(|failure| {
            let props = location
                .captures(text(failure, "stackTrace"))
                .map(|c| {
                    format!(
                        " file={},line={}",
                        escape_command(&c[1])
                            .replace(',', "%2C")
                            .replace(':', "%3A"),
                        &c[2]
                    )
                })
                .unwrap_or_default();
            format!(
                "::error{props}::{}: {}",
                escape_command(text(failure, "testName")),
                escape_command(text(failure, "message"))
            )
        })
        .collect()
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;

    #[test]
    fn junit_time_is_millisecond_precision_for_xunit_schema() {
        let report = junit(
            &json!({"tests":[{"name":"Pass","fullName":"Suite.Pass","status":"Passed","duration":0.0100718}]}),
        );
        assert_eq!(report.matches("time=\"0.010\"").count(), 2);
    }

    #[test]
    fn parent_suite_rollups_do_not_duplicate_leaf_failures() {
        let result = json!({"success":false,"tests":[{"name":"Fail","fullName":"Suite.Fail","status":"Failed","message":"oops"}],"failures":[
            {"testName":"Suite.Fail","message":"oops"},
            {"testName":"Suite","message":"One or more child tests had errors"},
            {"testName":"Project","message":"One or more child tests had errors"}
        ]});
        assert_eq!(junit(&result).matches("<failure ").count(), 1);
        assert_eq!(annotations(&result).len(), 1);
    }

    #[test]
    fn nunit_nests_test_cases_in_a_suite_and_preserves_details() {
        let report = nunit(
            &json!({"success":false,"tests":[{"name":"Fail","fullName":"Suite.Fail","status":"Failed","message":"oops","output":"log"}],"failures":[{"testName":"Suite.Fail","stackTrace":"at Suite.Fail:42"}]}),
        );
        assert!(report.contains("<test-suite "));
        assert!(report.contains("<stack-trace>at Suite.Fail:42</stack-trace>"));
        assert!(report.contains("<output>log</output>"));
        assert!(report.ends_with("</test-suite></test-run>\n"));
    }

    #[test]
    fn junit_preserves_failed_skipped_and_escaped_details() {
        let report = junit(&json!({"tests": [
            {"name":"Pass", "fullName":"Suite.Pass", "status":"Passed", "duration":0.1},
            {"name":"Fail<&", "fullName":"Suite.Fail", "status":"Failed", "duration":0.2,"message":"bad <value> & detail","output":"log"},
            {"name":"Skip", "fullName":"Suite.Skip", "status":"Skipped", "duration":0.0}
        ]}));
        assert!(report.contains("tests=\"3\""));
        assert!(report.contains("failures=\"1\""));
        assert_eq!(report.matches("<failure ").count(), 1);
        assert!(report.contains("bad &lt;value&gt; &amp; detail"));
        assert!(report.contains("<skipped"));
        assert!(report.contains("<system-out>log</system-out>"));
    }

    #[test]
    fn github_annotations_extract_unity_location_and_escape_injection() {
        let lines = annotations(
            &json!({"failures":[{"testName":"Suite.Fail", "message":"bad%\n::warning::oops", "stackTrace":"at Suite.Fail () (at Assets/Tests/My,Test.cs:42)"}]}),
        );
        assert_eq!(lines, vec!["::error file=Assets/Tests/My%2CTest.cs,line=42::Suite.Fail: bad%25%0A::warning::oops"]);
    }

    #[test]
    fn suite_failure_is_reported_even_without_failed_leaves() {
        let report = junit(
            &json!({"success":false,"tests":[],"failures":[{"testName":"Setup", "message":"setup failed"}]}),
        );
        assert!(report.contains("failures=\"1\""));
        assert!(report.contains("setup failed"));
    }
}
