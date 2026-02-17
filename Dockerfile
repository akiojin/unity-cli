FROM node:22-bookworm

# Build-time auth tokens for GitHub CLI (passed via docker compose build args)
ARG GH_TOKEN
ARG GITHUB_TOKEN

RUN apt-get update && apt-get install -y \
    build-essential \
    git \
    jq \
    ripgrep \
    curl \
    dos2unix \
    ca-certificates \
    gnupg \
    vim \
    tmux \
    && apt-get clean \
    && rm -rf /var/lib/apt/lists/*

# Install Rust toolchain
ENV CARGO_HOME=/root/.cargo
ENV RUSTUP_HOME=/root/.rustup
ENV PATH="${CARGO_HOME}/bin:${PATH}"
RUN curl --proto '=https' --tlsv1.2 -sSf https://sh.rustup.rs \
    | sh -s -- -y --profile minimal --default-toolchain stable \
    && rustc --version \
    && cargo --version

# Install GitHub CLI
RUN curl -fsSL https://cli.github.com/packages/githubcli-archive-keyring.gpg | dd of=/usr/share/keyrings/githubcli-archive-keyring.gpg \
    && chmod go+r /usr/share/keyrings/githubcli-archive-keyring.gpg \
    && echo "deb [arch=$(dpkg --print-architecture) signed-by=/usr/share/keyrings/githubcli-archive-keyring.gpg] https://cli.github.com/packages stable main" | tee /etc/apt/sources.list.d/github-cli.list > /dev/null \
    && apt update \
    && apt install gh -y \
    && apt-get clean \
    && rm -rf /var/lib/apt/lists/*

# Install GitHub CLI extensions (requires GH_TOKEN or GITHUB_TOKEN build arg)
RUN set -e; \
    TOKEN="${GH_TOKEN:-${GITHUB_TOKEN:-}}"; \
    if [ -z "$TOKEN" ]; then \
        echo "GH_TOKEN or GITHUB_TOKEN build arg is required to install gh extensions." >&2; \
        exit 1; \
    fi; \
    GH_TOKEN="$TOKEN" GITHUB_TOKEN="$TOKEN" gh extension install twelvelabs/gh-repo-config; \
    gh auth logout -h github.com >/dev/null 2>&1 || true

# Install .NET 9 SDK (for C# LSP build) via official install script
ENV DOTNET_ROOT=/usr/share/dotnet
ENV PATH="${DOTNET_ROOT}:${PATH}"
RUN set -eux; \
    curl -fsSL -o /tmp/dotnet-install.sh https://dot.net/v1/dotnet-install.sh; \
    chmod +x /tmp/dotnet-install.sh; \
    /tmp/dotnet-install.sh --channel 9.0 --install-dir "$DOTNET_ROOT"; \
    ln -sf "$DOTNET_ROOT/dotnet" /usr/bin/dotnet; \
    dotnet --info

# Install CLI tools (uv/uvx, Bun, Claude Code)
RUN bash -o pipefail -c "curl -fsSL https://astral.sh/uv/install.sh | bash \
    && curl -fsSL https://bun.sh/install | bash \
    && curl -fsSL https://claude.ai/install.sh | bash"
ENV PATH="/root/.cargo/bin:/root/.bun/bin:/root/.claude/bin:${PATH}"

# Claude Code EXDEV workaround (Issue #14799)
# Prevents cross-device link error when /root/.claude and /tmp are on different filesystems
ENV TMPDIR=/root/.claude/tmp
RUN mkdir -p /root/.claude/tmp

WORKDIR /unity-cli

# Node.js依存（corepack + pnpm）
COPY package.json pnpm-lock.yaml ./
RUN corepack enable && pnpm install --frozen-lockfile

# Use bash to invoke entrypoint to avoid exec-bit and CRLF issues on Windows mounts
ENTRYPOINT ["bash", "/unity-cli/scripts/entrypoint.sh"]
CMD ["tmux", "-u"]
