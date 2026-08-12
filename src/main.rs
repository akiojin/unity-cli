mod app;
mod cli;
mod core;
mod daemon;
mod lsp;
mod mcp;
mod reference;
mod skills;
#[cfg(test)]
mod test_env;
mod tooling;
mod unity;

pub use crate::core::config;
pub use crate::core::instances;
pub use crate::core::managed_binaries as lsp_manager;
pub use crate::daemon::unityd;
pub use crate::lsp::daemon as lspd;
pub use crate::tooling::local_tools;
pub use crate::tooling::tool_catalog;
pub use crate::unity::transport;

#[tokio::main]
async fn main() {
    if let Err(error) = app::runner::run().await {
        eprintln!("Error: {error:#}");
        std::process::exit(1);
    }
}
