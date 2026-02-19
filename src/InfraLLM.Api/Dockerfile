FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview AS base
WORKDIR /app
EXPOSE 8080

# Install runtime dependencies + stdio MCP server runtimes:
#   - nodejs/npm/npx  → Node.js-based MCP servers (most community servers)
#   - python3/pip     → Python-based MCP servers
#   - uv/uvx          → Python package runner (like npx for Python, used by ha-mcp etc.)
RUN apt-get update \
	&& apt-get install -y --no-install-recommends \
		libgssapi-krb5-2 \
		ca-certificates \
		curl \
		nodejs \
		npm \
		python3 \
		python3-pip \
	&& rm -rf /var/lib/apt/lists/* \
	&& curl -LsSf https://astral.sh/uv/install.sh | sh

ENV PATH="/root/.local/bin:$PATH"
# Pin the uv cache to a persistent directory so downloaded packages survive
# across uvx invocations within the same container lifetime.
ENV UV_CACHE_DIR="/opt/uv-cache"
# Pre-compile Python bytecode during uvx install so there's no slow .pyc
# compilation on first use (avoids multi-minute startup delays in Docker).
ENV UV_COMPILE_BYTECODE="1"
# Disable Python's block-buffering when stdout is a pipe (Docker/subprocess).
# Without this, Python flushes stdout lazily, which can cause MCP responses
# to sit in the buffer for a long time before being read by our C# reader.
ENV PYTHONUNBUFFERED="1"

FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /src
COPY src/InfraLLM.Core/InfraLLM.Core.csproj InfraLLM.Core/
COPY src/InfraLLM.Infrastructure/InfraLLM.Infrastructure.csproj InfraLLM.Infrastructure/
COPY src/InfraLLM.Api/InfraLLM.Api.csproj InfraLLM.Api/
RUN dotnet restore InfraLLM.Api/InfraLLM.Api.csproj
COPY src/ .
RUN dotnet publish InfraLLM.Api/InfraLLM.Api.csproj -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "InfraLLM.Api.dll"]
