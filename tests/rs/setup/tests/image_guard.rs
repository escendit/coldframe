//! FR-1 input check: the Hub image must not carry Wi-Fi credentials, so nothing that goes into it
//! may read them in at build time. This test scans the sources the image links (the Hub crate and
//! `packages/rs/{setup,protocol,crypto,hal}`) and their build scripts: an environment read other
//! than Cargo's `CARGO_PKG_*`, `OUT_DIR` or `CARGO_MANIFEST_DIR`, any `include_str!` or
//! `include_bytes!`, and an `[env]` table in the Hub's Cargo configuration all fail it.
//! `apps/rs/hub/check-image.sh` checks the built image for canary values. Neither proves FR-1 on
//! its own; together they catch the build-time ways a credential could get in.

use std::fs;
use std::path::{Path, PathBuf};

fn repo() -> PathBuf {
    Path::new(env!("CARGO_MANIFEST_DIR")).join("../../..")
}

fn hub() -> PathBuf {
    repo().join("apps/rs/hub")
}

/// Every crate the Hub image links from this repository.
fn image_crates() -> Vec<PathBuf> {
    let repo = repo();
    vec![
        repo.join("apps/rs/hub"),
        repo.join("packages/rs/setup"),
        repo.join("packages/rs/protocol"),
        repo.join("packages/rs/crypto"),
        repo.join("packages/rs/hal"),
    ]
}

/// Environment variables Cargo itself sets, which carry no user input.
fn allowed_variable(name: &str) -> bool {
    name.starts_with("CARGO_PKG_") || name == "OUT_DIR" || name == "CARGO_MANIFEST_DIR"
}

fn rust_sources(dir: &Path, out: &mut Vec<PathBuf>) {
    for entry in fs::read_dir(dir).expect("readable directory") {
        let path = entry.expect("directory entry").path();
        if path.is_dir() {
            rust_sources(&path, out);
        } else if path.extension().is_some_and(|ext| ext == "rs") {
            out.push(path);
        }
    }
}

/// Every build-time environment read of a variable Cargo does not own, and every
/// `include_str!`/`include_bytes!`.
fn forbidden_env_reads(text: &str) -> Vec<String> {
    let mut found = Vec::new();
    let mut flag = |index: usize| {
        let line = text[..index].lines().count().max(1);
        let snippet: String = text[index..].chars().take(40).collect();
        found.push(format!("line {line}: {snippet}"));
    };
    for pattern in ["env!(", "env::var(", "env::var_os("] {
        for (index, _) in text.match_indices(pattern) {
            let argument = text[index + pattern.len()..].trim_start();
            let name = argument
                .strip_prefix('"')
                .and_then(|rest| rest.split('"').next());
            if !name.is_some_and(allowed_variable) {
                flag(index);
            }
        }
    }
    for pattern in ["include_str!(", "include_bytes!("] {
        for (index, _) in text.match_indices(pattern) {
            flag(index);
        }
    }
    found
}

/// Whether a TOML file has an `[env]` table.
fn has_env_table(text: &str) -> bool {
    text.lines().any(|line| {
        let line = line.trim();
        line == "[env]"
            || line.starts_with("[env.")
            || line.starts_with("env.")
            || line.starts_with("env =")
    })
}

#[test]
fn image_sources_read_no_build_environment() {
    let mut files = Vec::new();
    for root in image_crates() {
        let before = files.len();
        rust_sources(&root.join("src"), &mut files);
        assert!(
            files.len() > before,
            "found the sources of {}",
            root.display()
        );
        let build = root.join("build.rs");
        if build.exists() {
            files.push(build);
        }
    }
    let mut problems = Vec::new();
    for file in &files {
        let text = fs::read_to_string(file).expect("readable source");
        for problem in forbidden_env_reads(&text) {
            problems.push(format!("{}: {problem}", file.display()));
        }
    }
    assert!(
        problems.is_empty(),
        "build-time environment reads: {problems:#?}"
    );
}

#[test]
fn hub_cargo_configuration_sets_no_env() {
    let root = hub();
    for file in [root.join("Cargo.toml"), root.join(".cargo/config.toml")] {
        let text = fs::read_to_string(&file).expect("readable manifest");
        assert!(
            !has_env_table(&text),
            "{} has an [env] table",
            file.display()
        );
    }
}

#[test]
fn the_guard_catches_what_it_should() {
    assert!(forbidden_env_reads(r#"const A: &str = env!("CARGO_PKG_VERSION");"#).is_empty());
    assert_eq!(forbidden_env_reads(r#"option_env!("WIFI_SSID")"#).len(), 1);
    assert_eq!(forbidden_env_reads(r#"env!( "PASSWORD")"#).len(), 1);
    assert_eq!(forbidden_env_reads("env!(CARGO_PKG_NAME)").len(), 1);
    assert!(forbidden_env_reads(r#"include!(concat!(env!("OUT_DIR"), "/a.rs"))"#).is_empty());
    assert_eq!(
        forbidden_env_reads(r#"std::env::var("WIFI_PASSWORD")"#).len(),
        1
    );
    assert!(forbidden_env_reads(r#"env::var("OUT_DIR")"#).is_empty());
    assert_eq!(forbidden_env_reads(r#"include_str!("wifi.txt")"#).len(), 1);
    assert_eq!(forbidden_env_reads(r#"include_bytes!("key.bin")"#).len(), 1);
    assert!(has_env_table("[build]\n[env]\nESP_LOG = \"info\"\n"));
    assert!(has_env_table("[env.WIFI]\n"));
    assert!(!has_env_table("[environment-free]\n# [env] in a comment\n"));
}
