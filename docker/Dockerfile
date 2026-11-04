FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/GroupDocs.Assembly.Mcp/GroupDocs.Assembly.Mcp.csproj \
    -c Release -o /app --no-self-contained

FROM base AS final

# Native dependencies GroupDocs.Assembly needs on Linux:
#   libgdiplus                — the presentation engine (PPTX/PPT/ODP templates, slides saved to
#                               PDF/XPS) draws through System.Drawing.Common 6.0.0, which P/Invokes
#                               GDI+. Pairs with <RuntimeHostConfigurationOption
#                               Include="System.Drawing.EnableUnixSupport" Value="true" /> in the csproj;
#                               without both, presentation templates fail with "The type initializer
#                               for 'Gdip' threw an exception". Word, spreadsheet and email templates
#                               draw through Aspose.Drawing and SkiaSharp and do not need it.
#   libfontconfig1, fontconfig — font discovery and the fc-cache tool
#   ttf-mscorefonts-installer — Arial / Times New Roman and the metric-compatible set Office
#                               templates reference, so PDF/XPS output lays text out with real
#                               fonts; EULA pre-accepted via debconf
# fc-cache rebuilds the font index. Mirrors the Linux step in the Tests repo's integration.yml.
USER root
RUN apt-get update \
 && echo "ttf-mscorefonts-installer msttcorefonts/accepted-mscorefonts-eula select true" | debconf-set-selections \
 && apt-get install -y --no-install-recommends libgdiplus libfontconfig1 fontconfig ttf-mscorefonts-installer \
 && fc-cache -f -v \
 && rm -rf /var/lib/apt/lists/*

# OCI image labels. VERSION is supplied by the publish workflow via
# --build-arg VERSION=YY.MM.N. Default 'dev' makes local `docker build` runs
# self-identify instead of inheriting the base image's misleading version.
ARG VERSION=dev
LABEL org.opencontainers.image.title="GroupDocs.Assembly.Mcp" \
      org.opencontainers.image.description="MCP server exposing GroupDocs.Assembly for .NET as AI-callable tools (AssembleDocument, AssemblePerRecord, GetTemplateFields, GetDocumentInfo, GetSupportedFormats) — generate Word, Excel, PowerPoint, PDF and email documents from templates and JSON, XML, CSV or spreadsheet data, for Claude, Cursor, GitHub Copilot, and other MCP agents." \
      org.opencontainers.image.version="${VERSION}" \
      org.opencontainers.image.source="https://github.com/groupdocs-assembly/GroupDocs.Assembly.Mcp" \
      org.opencontainers.image.url="https://www.nuget.org/packages/GroupDocs.Assembly.Mcp" \
      org.opencontainers.image.licenses="MIT" \
      org.opencontainers.image.vendor="Aspose Pty Ltd"

COPY --from=build /app .

ENV GROUPDOCS_MCP_STORAGE_PATH=/data
VOLUME /data

# Non-root. On a Linux host, run the container as your own user so it can write into a bind
# mount you own: docker run --user "$(id -u):$(id -g)" ...
USER $APP_UID

ENTRYPOINT ["dotnet", "GroupDocs.Assembly.Mcp.dll"]
