import { defineConfig } from "vitest/config";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";
import path from "path";
import { extensionRuntimeModules, extensionRuntimeVersion } from "./scripts/extension-runtime-contract.ts";

const extensionRuntimeEntries = Object.fromEntries(
  extensionRuntimeModules.map((definition) => [
    `extension-runtime-${definition.id}`,
    path.resolve(__dirname, `./src/generated/extensions/runtime/${extensionRuntimeVersion}/${definition.sourceFileName}`),
  ])
);

const extensionRuntimeFileNames = new Map<string, string>(
  extensionRuntimeModules.map((definition) => [
    `extension-runtime-${definition.id}`,
    `assets/extension-runtime/${extensionRuntimeVersion}/${definition.outputFileName}`,
  ])
);

function buildExtensionImportMap(useDevRuntimeModules: boolean) {
  return Object.fromEntries(
    extensionRuntimeModules.flatMap((definition) => {
      const target = useDevRuntimeModules
        ? `/src/generated/extensions/runtime/${extensionRuntimeVersion}/${definition.sourceFileName}`
        : `/${extensionRuntimeFileNames.get(`extension-runtime-${definition.id}`)!}`;
      return [definition.specifier, ...definition.legacySpecifiers].map((specifier) => [specifier, target]);
    })
  );
}

function extensionRuntimeImportMapPlugin(useDevRuntimeModules: boolean) {
  return {
    name: "extension-runtime-import-map",
    transformIndexHtml() {
      const importMap = JSON.stringify({ imports: buildExtensionImportMap(useDevRuntimeModules) }, null, 2);
      return [
        {
          tag: "meta",
          attrs: {
            name: "cove-extension-runtime-version",
            content: extensionRuntimeVersion,
          },
          injectTo: "head",
        },
        {
          tag: "script",
          attrs: {
            type: "importmap",
          },
          children: importMap,
          injectTo: "head",
        },
      ];
    },
  };
}

export default defineConfig(({ command }) => {
  const useDevRuntimeModules = command === "serve";

  return {
    plugins: [react(), tailwindcss(), extensionRuntimeImportMapPlugin(useDevRuntimeModules)],
    resolve: {
      alias: {
        "@": path.resolve(__dirname, "./src"),
      },
    },
    server: {
      host: "127.0.0.1",
      port: 5173,
      // Allow importing files from the repo root (e.g. CHANGELOG.md) during dev.
      fs: {
        allow: [path.resolve(__dirname, "..")],
      },
      proxy: {
        "/api": {
          target: "http://localhost:5073",
          changeOrigin: true,
        },
        "/hubs": {
          target: "http://localhost:5073",
          changeOrigin: true,
          ws: true,
        },
      },
    },
    build: {
      outDir: "../src/Cove.Api/wwwroot",
      emptyOutDir: true,
      rollupOptions: {
        preserveEntrySignatures: "strict",
        input: {
          index: path.resolve(__dirname, "./index.html"),
          ...extensionRuntimeEntries,
        },
        output: {
          entryFileNames: (chunkInfo) => extensionRuntimeFileNames.get(chunkInfo.name) ?? "assets/[name]-[hash].js",
          manualChunks: {
            vendor: ["react", "react-dom", "@tanstack/react-query"],
            icons: ["lucide-react"],
            signalr: ["@microsoft/signalr"],
          },
        },
      },
    },
    test: {
      globals: true,
      environment: "jsdom",
      setupFiles: "./src/test/setup.ts",
      css: true,
    },
  };
});
