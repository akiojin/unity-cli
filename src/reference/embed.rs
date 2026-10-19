use std::fs;
use std::path::{Path, PathBuf};

use anyhow::{anyhow, Context, Result};
use serde::{Deserialize, Serialize};

use crate::reference::index::ReferenceSymbolIndex;
use crate::reference::search;

pub const EMBEDDING_INDEX_REL_PATH: &str = ".unity-cli-index/embeddings.bin";
pub const EMBEDDING_INDEX_VERSION: u32 = 1;
pub const DEFAULT_MODEL_ID: &str = "BAAI/bge-small-en-v1.5";
pub const DEFAULT_VIEW_LINES: u32 = 20;
pub const DEFAULT_TOP_K: usize = 10;

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
pub struct EmbeddedSymbol {
    pub symbol: String,
    pub kind: String,
    pub path: String,
    pub line: u32,
    pub vector: Vec<f32>,
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
pub struct EmbeddingIndex {
    pub version: u32,
    pub model_id: String,
    pub dim: usize,
    pub items: Vec<EmbeddedSymbol>,
}

pub trait Embedder {
    fn embed(&self, texts: &[String]) -> Result<Vec<Vec<f32>>>;
    fn model_id(&self) -> &str;
}

pub struct FastEmbedder {
    inner: std::cell::RefCell<fastembed::TextEmbedding>,
    model_id: String,
}

impl FastEmbedder {
    pub fn new() -> Result<Self> {
        Self::with_model(fastembed::EmbeddingModel::BGESmallENV15)
    }

    pub fn with_model(model: fastembed::EmbeddingModel) -> Result<Self> {
        // Intel macOS loads ONNX Runtime dynamically and `ort` panics when the
        // dylib is missing, so resolve it up front and fail with guidance.
        #[cfg(all(target_os = "macos", target_arch = "x86_64"))]
        {
            let dylib = resolve_onnxruntime_dylib(
                std::env::var("ORT_DYLIB_PATH").ok(),
                &[
                    Path::new("/usr/local/opt/onnxruntime/lib/libonnxruntime.dylib"),
                    Path::new("/usr/local/lib/libonnxruntime.dylib"),
                ],
            )?;
            std::env::set_var("ORT_DYLIB_PATH", dylib);
        }
        let model_id = format!("{model:?}");
        let options = fastembed::TextInitOptions::new(model);
        let inner = fastembed::TextEmbedding::try_new(options)
            .map_err(|e| anyhow!("failed to initialize embedding model: {e}"))?;
        Ok(Self {
            inner: std::cell::RefCell::new(inner),
            model_id,
        })
    }
}

/// Resolves the ONNX Runtime dylib for builds that load it dynamically: an
/// explicit `ORT_DYLIB_PATH` must exist, otherwise the first existing
/// candidate (Homebrew locations) is used.
#[cfg_attr(
    not(all(target_os = "macos", target_arch = "x86_64")),
    allow(dead_code)
)]
fn resolve_onnxruntime_dylib(env_path: Option<String>, candidates: &[&Path]) -> Result<PathBuf> {
    if let Some(path) = env_path.filter(|value| !value.trim().is_empty()) {
        let path = PathBuf::from(path.trim());
        if path.is_file() {
            return Ok(path);
        }
        return Err(anyhow!(
            "ORT_DYLIB_PATH points to a missing ONNX Runtime library: {}",
            path.display()
        ));
    }
    candidates
        .iter()
        .find(|candidate| candidate.is_file())
        .map(|candidate| candidate.to_path_buf())
        .ok_or_else(|| {
            anyhow!(
                "ONNX Runtime is required for embeddings on this platform: \
                 run `brew install onnxruntime` or set ORT_DYLIB_PATH to libonnxruntime.dylib"
            )
        })
}

impl Embedder for FastEmbedder {
    fn embed(&self, texts: &[String]) -> Result<Vec<Vec<f32>>> {
        if texts.is_empty() {
            return Ok(Vec::new());
        }
        self.inner
            .borrow_mut()
            .embed(texts, None)
            .map_err(|e| anyhow!("embedding failed: {e}"))
    }

    fn model_id(&self) -> &str {
        &self.model_id
    }
}

pub fn symbol_to_text(
    symbol_name: &str,
    namespace: Option<&str>,
    kind: &str,
    view_excerpt: &[String],
) -> String {
    let header = match namespace {
        Some(ns) => format!("{ns}.{symbol_name} ({kind})"),
        None => format!("{symbol_name} ({kind})"),
    };
    let body = view_excerpt.join("\n");
    if body.is_empty() {
        header
    } else {
        format!("{header}\n{body}")
    }
}

pub fn build_embedding_index<E: Embedder>(
    version_dir: &Path,
    embedder: &E,
    symbol_index: &ReferenceSymbolIndex,
) -> Result<EmbeddingIndex> {
    let mut texts: Vec<String> = Vec::new();
    let mut metas: Vec<(String, String, String, u32)> = Vec::new();
    for file in symbol_index.files.values() {
        for sym in &file.symbols {
            let view = search::run_view(
                version_dir,
                &sym.path,
                Some(sym.line),
                Some(DEFAULT_VIEW_LINES),
            )
            .map(|v| v.lines)
            .unwrap_or_default();
            let text = symbol_to_text(&sym.name, sym.namespace.as_deref(), &sym.kind, &view);
            let fqn = sym.fqn.clone().unwrap_or_else(|| sym.name.clone());
            texts.push(text);
            metas.push((fqn, sym.kind.clone(), sym.path.clone(), sym.line));
        }
    }
    if texts.is_empty() {
        return Ok(EmbeddingIndex {
            version: EMBEDDING_INDEX_VERSION,
            model_id: embedder.model_id().to_string(),
            dim: 0,
            items: vec![],
        });
    }
    let vectors = embedder.embed(&texts)?;
    if vectors.len() != texts.len() {
        return Err(anyhow!(
            "embedder returned {} vectors for {} texts",
            vectors.len(),
            texts.len()
        ));
    }
    let dim = vectors.first().map(|v| v.len()).unwrap_or(0);
    let mut items = Vec::with_capacity(metas.len());
    for ((fqn, kind, path, line), vector) in metas.into_iter().zip(vectors) {
        items.push(EmbeddedSymbol {
            symbol: fqn,
            kind,
            path,
            line,
            vector,
        });
    }
    Ok(EmbeddingIndex {
        version: EMBEDDING_INDEX_VERSION,
        model_id: embedder.model_id().to_string(),
        dim,
        items,
    })
}

pub fn save_embedding_index(path: &Path, index: &EmbeddingIndex) -> Result<()> {
    if let Some(parent) = path.parent() {
        fs::create_dir_all(parent)
            .with_context(|| format!("failed to create {}", parent.display()))?;
    }
    let bytes = bincode::serialize(index).context("failed to serialize embedding index")?;
    fs::write(path, bytes).with_context(|| format!("failed to write {}", path.display()))?;
    Ok(())
}

pub fn load_embedding_index(path: &Path) -> Result<EmbeddingIndex> {
    let bytes = fs::read(path).with_context(|| format!("failed to read {}", path.display()))?;
    let index: EmbeddingIndex = bincode::deserialize(&bytes)
        .with_context(|| format!("failed to parse embedding index at {}", path.display()))?;
    Ok(index)
}

pub fn search(
    index: &EmbeddingIndex,
    query_vec: &[f32],
    top_k: usize,
) -> Vec<(EmbeddedSymbol, f32)> {
    if index.items.is_empty() || query_vec.is_empty() {
        return Vec::new();
    }
    let mut scored: Vec<(EmbeddedSymbol, f32)> = index
        .items
        .iter()
        .map(|item| (item.clone(), cosine_similarity(query_vec, &item.vector)))
        .collect();
    scored.sort_by(|a, b| b.1.partial_cmp(&a.1).unwrap_or(std::cmp::Ordering::Equal));
    if scored.len() > top_k {
        scored.truncate(top_k);
    }
    scored
}

pub fn cosine_similarity(a: &[f32], b: &[f32]) -> f32 {
    if a.is_empty() || b.is_empty() || a.len() != b.len() {
        return 0.0;
    }
    let mut dot = 0.0f32;
    let mut na = 0.0f32;
    let mut nb = 0.0f32;
    for (x, y) in a.iter().zip(b.iter()) {
        dot += x * y;
        na += x * x;
        nb += y * y;
    }
    if na == 0.0 || nb == 0.0 {
        return 0.0;
    }
    dot / (na.sqrt() * nb.sqrt())
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::collections::BTreeMap;
    use tempfile::TempDir;

    use crate::reference::index::{IndexedFile, ReferenceSymbolEntry, ReferenceSymbolIndex};

    struct MockEmbedder {
        model_id: String,
        dim: usize,
    }

    impl Embedder for MockEmbedder {
        fn embed(&self, texts: &[String]) -> Result<Vec<Vec<f32>>> {
            let mut out = Vec::with_capacity(texts.len());
            for (i, text) in texts.iter().enumerate() {
                let mut v = vec![0.0f32; self.dim];
                for (j, c) in text.chars().enumerate() {
                    if j >= self.dim {
                        break;
                    }
                    v[j] = (c as u32 as f32) * 0.001 + (i as f32) * 0.01;
                }
                out.push(v);
            }
            Ok(out)
        }

        fn model_id(&self) -> &str {
            &self.model_id
        }
    }

    fn make_fixture_index() -> ReferenceSymbolIndex {
        let mut files = BTreeMap::new();
        files.insert(
            "Runtime/Foo.cs".to_string(),
            IndexedFile {
                signature: "1:1".to_string(),
                symbols: vec![ReferenceSymbolEntry {
                    path: "Runtime/Foo.cs".to_string(),
                    name: "Foo".to_string(),
                    kind: "class".to_string(),
                    line: 1,
                    namespace: Some("UnityEngine".to_string()),
                    container: None,
                    fqn: Some("UnityEngine.Foo".to_string()),
                }],
            },
        );
        ReferenceSymbolIndex {
            version: 1,
            unity_version: None,
            branch: None,
            generated_at_epoch_ms: 0,
            files,
        }
    }

    #[test]
    fn resolve_onnxruntime_dylib_prefers_existing_env_path() {
        let tmp = TempDir::new().unwrap();
        let env_lib = tmp.path().join("custom.dylib");
        let brew_lib = tmp.path().join("brew.dylib");
        fs::write(&env_lib, b"").unwrap();
        fs::write(&brew_lib, b"").unwrap();

        let resolved = resolve_onnxruntime_dylib(
            Some(env_lib.to_string_lossy().to_string()),
            &[brew_lib.as_path()],
        )
        .unwrap();
        assert_eq!(resolved, env_lib);
    }

    #[test]
    fn resolve_onnxruntime_dylib_rejects_missing_env_path() {
        let tmp = TempDir::new().unwrap();
        let brew_lib = tmp.path().join("brew.dylib");
        fs::write(&brew_lib, b"").unwrap();
        let missing = tmp.path().join("missing.dylib");

        let error = resolve_onnxruntime_dylib(
            Some(missing.to_string_lossy().to_string()),
            &[brew_lib.as_path()],
        )
        .unwrap_err();
        assert!(error.to_string().contains("ORT_DYLIB_PATH"));
    }

    #[test]
    fn resolve_onnxruntime_dylib_falls_back_to_first_existing_candidate() {
        let tmp = TempDir::new().unwrap();
        let absent = tmp.path().join("absent.dylib");
        let present = tmp.path().join("present.dylib");
        fs::write(&present, b"").unwrap();

        let resolved =
            resolve_onnxruntime_dylib(Some(String::new()), &[absent.as_path(), present.as_path()])
                .unwrap();
        assert_eq!(resolved, present);
    }

    #[test]
    fn resolve_onnxruntime_dylib_explains_how_to_install_when_missing() {
        let tmp = TempDir::new().unwrap();
        let absent = tmp.path().join("absent.dylib");

        let error = resolve_onnxruntime_dylib(None, &[absent.as_path()]).unwrap_err();
        let message = error.to_string();
        assert!(message.contains("brew install onnxruntime"), "{message}");
        assert!(message.contains("ORT_DYLIB_PATH"), "{message}");
    }

    #[test]
    fn symbol_to_text_includes_namespace_and_kind() {
        let text = symbol_to_text(
            "Animator",
            Some("UnityEngine"),
            "class",
            &["{".to_string(), "}".to_string()],
        );
        assert!(text.contains("UnityEngine.Animator"));
        assert!(text.contains("(class)"));
        assert!(text.contains("{"));
    }

    #[test]
    fn symbol_to_text_without_namespace_omits_dot() {
        let text = symbol_to_text("Foo", None, "method", &[]);
        assert_eq!(text, "Foo (method)");
    }

    #[test]
    fn embedding_roundtrip_via_bincode() {
        let tmp = TempDir::new().unwrap();
        let path = tmp.path().join(EMBEDDING_INDEX_REL_PATH);
        let index = EmbeddingIndex {
            version: EMBEDDING_INDEX_VERSION,
            model_id: "test-model".to_string(),
            dim: 3,
            items: vec![EmbeddedSymbol {
                symbol: "Foo".to_string(),
                kind: "class".to_string(),
                path: "Runtime/Foo.cs".to_string(),
                line: 5,
                vector: vec![0.1, 0.2, 0.3],
            }],
        };
        save_embedding_index(&path, &index).unwrap();
        let loaded = load_embedding_index(&path).unwrap();
        assert_eq!(loaded, index);
    }

    #[test]
    fn embedding_reads_fastembed6_bincode1_fixture() {
        // Written by save_embedding_index with fastembed 6.1.0 and bincode 1.3.3
        // before the fastembed 7 migration. Do not regenerate with the new writer.
        let bytes =
            include_bytes!("../../tests/fixtures/reference-embedding/fastembed6-bincode1.bin");
        let tmp = TempDir::new().unwrap();
        let path = tmp.path().join(EMBEDDING_INDEX_REL_PATH);
        fs::create_dir_all(path.parent().unwrap()).unwrap();
        fs::write(&path, bytes).unwrap();

        let index = load_embedding_index(&path).unwrap();
        let model = fastembed::EmbeddingModel::BGESmallENV15;
        let model_info = fastembed::TextEmbedding::get_model_info(&model).unwrap();
        assert_eq!(index.version, EMBEDDING_INDEX_VERSION);
        assert_eq!(index.model_id, format!("{model:?}"));
        assert_eq!(index.dim, model_info.dim);
        assert_eq!(index.items.len(), 1);
        let item = &index.items[0];
        assert_eq!(item.symbol, "UnityEngine.Animator");
        assert_eq!(item.kind, "class");
        assert_eq!(item.path, "Runtime/Export/Animation/Animator.bindings.cs");
        assert_eq!(item.line, 42);
        let mut query = vec![0.0; model_info.dim];
        query[0] = 1.0;
        assert_eq!(item.vector, query);
        let hits = search(&index, &query, 1);
        assert_eq!(hits[0].0, *item);
        assert!((hits[0].1 - 1.0).abs() < f32::EPSILON);

        // Updating an existing index must also retain its on-disk format.
        save_embedding_index(&path, &index).unwrap();
        assert_eq!(fs::read(&path).unwrap(), bytes);
    }

    #[test]
    fn search_ranks_by_cosine_similarity() {
        let index = EmbeddingIndex {
            version: EMBEDDING_INDEX_VERSION,
            model_id: "m".to_string(),
            dim: 3,
            items: vec![
                EmbeddedSymbol {
                    symbol: "Far".to_string(),
                    kind: "class".to_string(),
                    path: "F.cs".to_string(),
                    line: 1,
                    vector: vec![1.0, 0.0, 0.0],
                },
                EmbeddedSymbol {
                    symbol: "Near".to_string(),
                    kind: "class".to_string(),
                    path: "N.cs".to_string(),
                    line: 1,
                    vector: vec![0.0, 1.0, 0.0],
                },
            ],
        };
        let query = vec![0.0, 1.0, 0.0];
        let hits = search(&index, &query, 1);
        assert_eq!(hits.len(), 1);
        assert_eq!(hits[0].0.symbol, "Near");
        assert!(hits[0].1 > 0.99);
    }

    #[test]
    fn search_returns_empty_for_empty_index() {
        let index = EmbeddingIndex {
            version: 1,
            model_id: "m".to_string(),
            dim: 0,
            items: vec![],
        };
        let hits = search(&index, &[1.0, 0.0], 10);
        assert!(hits.is_empty());
    }

    #[test]
    fn search_returns_empty_for_empty_query() {
        let index = EmbeddingIndex {
            version: 1,
            model_id: "m".to_string(),
            dim: 3,
            items: vec![EmbeddedSymbol {
                symbol: "X".to_string(),
                kind: "class".to_string(),
                path: "x.cs".to_string(),
                line: 1,
                vector: vec![1.0, 0.0, 0.0],
            }],
        };
        let hits = search(&index, &[], 10);
        assert!(hits.is_empty());
    }

    #[test]
    fn cosine_similarity_handles_zero_vectors() {
        assert_eq!(cosine_similarity(&[0.0; 3], &[1.0, 0.0, 0.0]), 0.0);
        assert_eq!(cosine_similarity(&[1.0, 2.0], &[1.0, 2.0, 3.0]), 0.0);
        assert!((cosine_similarity(&[1.0, 0.0], &[1.0, 0.0]) - 1.0).abs() < 1e-6);
    }

    #[test]
    fn build_embedding_index_with_mock_embedder() {
        let tmp = TempDir::new().unwrap();
        let cs_dir = tmp.path().join("Runtime");
        std::fs::create_dir_all(&cs_dir).unwrap();
        std::fs::write(
            cs_dir.join("Foo.cs"),
            "namespace UnityEngine { public class Foo {} }\n",
        )
        .unwrap();
        let symbol_index = make_fixture_index();
        let embedder = MockEmbedder {
            model_id: "mock".to_string(),
            dim: 5,
        };
        let index = build_embedding_index(tmp.path(), &embedder, &symbol_index).unwrap();
        assert_eq!(index.version, EMBEDDING_INDEX_VERSION);
        assert_eq!(index.model_id, "mock");
        assert_eq!(index.dim, 5);
        assert_eq!(index.items.len(), 1);
        let first = &index.items[0];
        assert_eq!(first.symbol, "UnityEngine.Foo");
        assert_eq!(first.kind, "class");
        assert_eq!(first.vector.len(), 5);
    }

    #[test]
    fn build_embedding_index_handles_empty_symbol_index() {
        let tmp = TempDir::new().unwrap();
        let symbol_index = ReferenceSymbolIndex::default();
        let embedder = MockEmbedder {
            model_id: "mock".to_string(),
            dim: 3,
        };
        let index = build_embedding_index(tmp.path(), &embedder, &symbol_index).unwrap();
        assert!(index.items.is_empty());
        assert_eq!(index.dim, 0);
    }
}
