# Matrix Design System Registry — Complete Guide

**Version:** 0.9.1  
**Registry namespace:** `@matrix`  
**Registry URL (local):** `http://localhost:3001`

This document is for everyone who touches the Matrix Design System Registry — whether you are **consuming** components in your own project or **maintaining** the registry itself.

---

## Table of Contents

1. [What is this?](#1-what-is-this)
2. [How it works — the full picture](#2-how-it-works--the-full-picture)
3. [For Consumers](#3-for-consumers)
   - [3.1 Prerequisites](#31-prerequisites)
   - [3.2 Quick start (4 steps)](#32-quick-start-4-steps)
   - [3.3 Step 1 — Configure your project](#33-step-1--configure-your-project)
   - [3.4 Step 2 — Start the registry server](#34-step-2--start-the-registry-server)
   - [3.5 Step 3 — Install the theme and fonts first](#35-step-3--install-the-theme-and-fonts-first)
   - [3.6 Step 4 — Install components](#36-step-4--install-components)
   - [3.7 Where do files land in my project?](#37-where-do-files-land-in-my-project)
   - [3.8 Listing what is available](#38-listing-what-is-available)
   - [3.9 Troubleshooting — Consumer](#39-troubleshooting--consumer)
4. [For Maintainers](#4-for-maintainers)
   - [4.1 Prerequisites](#41-prerequisites)
   - [4.2 Repository structure](#42-repository-structure)
   - [4.3 Running the registry](#43-running-the-registry)
   - [4.4 How registry.json works](#44-how-registryjson-works)
   - [4.5 Item types at a glance](#45-item-types-at-a-glance)
   - [4.6 registry.json field reference](#46-registryjson-field-reference)
   - [4.7 Add a vendored shadcn component](#47-add-a-vendored-shadcn-component)
   - [4.8 Add a Matrix custom component](#48-add-a-matrix-custom-component)
   - [4.9 Add an external third-party component](#49-add-an-external-third-party-component)
   - [4.10 Update an existing component](#410-update-an-existing-component)
   - [4.11 Add a live preview](#411-add-a-live-preview)
   - [4.12 Verification checklist](#412-verification-checklist)
   - [4.13 Release checklist](#413-release-checklist)
   - [4.14 Troubleshooting — Maintainer](#414-troubleshooting--maintainer)
5. [Registry policy](#5-registry-policy)
6. [Reference](#6-reference)

---

## 1. What is this?

The Matrix Design System Registry is a **central store** of UI components, themes, and fonts for Matrix products. It is built on top of the [shadcn registry](https://ui.shadcn.com/docs/registry) system.

**Why does it exist?**  
Instead of every project separately pulling components from shadcn, community sources, or copying code manually, all projects pull from one controlled place — the `@matrix` registry. Maintainers decide what goes in and review everything before it reaches any consumer project.

**Two kinds of items live in this registry:**

| Item kind | What it means |
|---|---|
| **Matrix-owned** | Files authored or fully controlled by the Matrix team. Includes the `trust-blue` theme, `font-geist`, `font-geist-mono`, and any custom Matrix components. |
| **Vendored shadcn components** | shadcn primitives (button, badge, separator, etc.) copied into this repository, reviewed, and re-served from here. They are not fetched from shadcn at install time — the source lives here. |

**One rule for consumers:** you only ever install from `@matrix`. You never need to know where a component originally came from.

---

## 2. How it works — the full picture

```
┌─────────────────────────────────────────────────────────────┐
│                    REGISTRY (maintainer side)                │
│                                                             │
│  shadcn upstream (GitHub)                                   │
│        │                                                    │
│        │  maintainer copies source manually                 │
│        ▼                                                    │
│  components/ui/<component>.tsx  ← file lives here          │
│  registry/blocks/<block>/       ← custom blocks here       │
│        │                                                    │
│        │  pnpm registry:build  (= shadcn build)            │
│        ▼                                                    │
│  public/r/<component>.json      ← served to consumers      │
└────────────────────────┬────────────────────────────────────┘
                         │  HTTP  (localhost:3001 or hosted URL)
                         │
┌────────────────────────▼────────────────────────────────────┐
│                    CONSUMER PROJECT                          │
│                                                             │
│  pnpm dlx shadcn@latest add @matrix/button                  │
│        │                                                    │
│        │  reads components.json → resolves @matrix URL      │
│        │  fetches /r/button.json from registry              │
│        │  downloads button.tsx from registry                │
│        ▼                                                    │
│  src/components/ui/button.tsx   ← file written here        │
│  (npm deps like @radix-ui/react-slot added to package.json) │
└─────────────────────────────────────────────────────────────┘
```

**Key point:** Everything comes from this registry. No component is fetched from shadcn's servers or any external URL during a consumer install. The `meta.links.source` field in `registry.json` is documentation only — it records where the original file came from. The CLI does not follow it.

---

## 3. For Consumers

### 3.1 Prerequisites

Before you start, make sure you have:

- **pnpm** installed — `npm install -g pnpm`
- A **React project** already created (Vite, Next.js, etc.)
- **shadcn initialized** in your project — this means a `components.json` file already exists in your project root. If it does not, run `pnpm dlx shadcn@latest init` first.
- **Someone running the registry server** — the registry must be running locally on port 3001 for installs to work. See [Step 2](#34-step-2--start-the-registry-server).

---

### 3.2 Quick start (4 steps)

```bash
# 1. Add @matrix to your components.json  (see Step 1 below)

# 2. Start the registry server (keep this terminal open)
pnpm dev -p 3001   # run this from inside the registry project

# 3. Install theme and fonts first
pnpm dlx shadcn@latest add @matrix/trust-blue @matrix/font-geist @matrix/font-geist-mono

# 4. Install any component
pnpm dlx shadcn@latest add @matrix/button
```

---

### 3.3 Step 1 — Configure your project

Open your project's `components.json` and add the `registries` field. This tells the shadcn CLI what `@matrix` means.

**Your `components.json` should look like this:**

```json
{
  "$schema": "https://ui.shadcn.com/schema.json",
  "style": "radix-nova",
  "rsc": false,
  "tsx": true,
  "tailwind": {
    "config": "",
    "css": "src/index.css",
    "baseColor": "neutral",
    "cssVariables": true,
    "prefix": ""
  },
  "iconLibrary": "lucide",
  "rtl": false,
  "menuColor": "default",
  "menuAccent": "subtle",
  "aliases": {
    "components": "@/components",
    "utils": "@/lib/utils",
    "ui": "@/components/ui",
    "lib": "@/lib",
    "hooks": "@/hooks"
  },
  "registries": {
    "@matrix": "http://localhost:3001/r/{name}.json"
  }
}
```

**Two fields that matter most:**

**`tailwind.css`** — This is the path to your main CSS file. The shadcn CLI injects CSS variables (theme colours, font settings) into this file when you install the theme. For standard Vite projects this is `src/index.css`. For Next.js it is usually `app/globals.css`. Make sure this path matches your actual project.

**`registries`** — This maps the `@matrix` shorthand to the registry URL. The `{name}` part is replaced by the component name automatically when you run an install command.

> **Note:** If your project already has a `components.json`, only add the `"registries"` block — do not change anything else unless you know what it does.

---

### 3.4 Step 2 — Start the registry server

The registry server must be running before you can install any component. Open a terminal, navigate to the **registry project folder**, and run:

```bash
pnpm dev -p 3001
```

You will see Next.js start up. Keep this terminal open while you work. The registry is now available at `http://localhost:3001`.

> **What this command does:** The `predev` hook automatically runs `pnpm registry:build` first, which compiles `registry.json` into individual component JSON files under `public/r/`. Then it starts the Next.js development server on port 3001.

---

### 3.5 Step 3 — Install the theme and fonts first

**This step is mandatory before installing any component.**

The `trust-blue` theme injects all the colour tokens and typography variables that every component depends on. If you install a button before installing the theme, it will render without any Matrix styling.

Run this in your **consumer project** (not the registry):

```bash
pnpm dlx shadcn@latest add @matrix/trust-blue @matrix/font-geist @matrix/font-geist-mono
```

This does three things:
- Injects the Trust Blue colour palette (light and dark mode) into your CSS file
- Adds the Geist Variable font and its CSS configuration
- Adds the Geist Mono Variable font and its CSS configuration

You only need to do this once per project.

---

### 3.6 Step 4 — Install components

With the theme in place, install any component from the registry:

```bash
# Install a single component
pnpm dlx shadcn@latest add @matrix/button

# Install multiple at once
pnpm dlx shadcn@latest add @matrix/button @matrix/badge @matrix/separator

# Install a full block (includes all its sub-components automatically)
pnpm dlx shadcn@latest add @matrix/dashboard-01
```

The CLI handles everything — it downloads the source file, installs any npm packages the component needs, and places files in the correct folders.

**Preview before installing:** If you want to see exactly what a command will write before it touches your project, add `--dry-run`:

```bash
pnpm dlx shadcn@latest add @matrix/dashboard-01 --dry-run
```

This lists every file that would be written and every package that would be installed, without actually doing anything.

---

### 3.7 Where do files land in my project?

This is determined by the `aliases` section in your `components.json`. Here is how it maps:

| Component type | Goes into |
|---|---|
| `registry:ui` (button, badge, input…) | `src/components/ui/` (your `ui` alias) |
| `registry:component` (complex components) | `src/components/` (your `components` alias) |
| `registry:hook` | `src/hooks/` (your `hooks` alias) |
| `registry:page` | The path set in `target` in the registry (e.g. `app/dashboard/page.tsx`) |
| `registry:file` | The path set in `target` in the registry |

**Example — installing button:**
```
registry defines: files[].path = "components/ui/button.tsx", type = "registry:ui"
your aliases.ui  = "@/components/ui"  →  resolves to  src/components/ui/
result: button.tsx lands at  src/components/ui/button.tsx
```

**Example — installing dashboard-01:**  
The dashboard block has a mix of types. `page.tsx` has an explicit `target: "app/dashboard/page.tsx"`, so it goes directly there. The sub-components go to `src/components/` via the `components` alias.

> **If files are landing in the wrong place:** Check your `aliases` in `components.json`. The `ui` alias must point to the folder where you want your shadcn primitives. For Vite the default is `@/components/ui`.

---

### 3.8 Listing what is available

To see all components currently published in the registry:

```bash
pnpm exec shadcn list http://localhost:3001/r/registry.json
```

> The registry must be running for this to work.

---

### 3.9 Troubleshooting — Consumer

**"Could not find component @matrix/button" or "Failed to fetch"**  
The registry is not running. Open a terminal in the registry project and run `pnpm dev -p 3001`. Keep it open while installing.

**"Component installed but has no styling"**  
You installed a component before installing the theme. Run:
```bash
pnpm dlx shadcn@latest add @matrix/trust-blue @matrix/font-geist @matrix/font-geist-mono
```
Then re-import your component — the styles should apply without reinstalling it.

**"Port 3001 is already in use"**  
Another process is using port 3001. Either stop that process, or find and kill it:
```bash
# On macOS / Linux
lsof -ti :3001 | xargs kill

# On Windows (PowerShell)
Get-Process -Id (Get-NetTCPConnection -LocalPort 3001).OwningProcess | Stop-Process
```

**"Files are being written to the wrong folder"**  
Check the `aliases` section in your `components.json`. The `ui` alias controls where `registry:ui` components go. It must resolve to the folder you expect (e.g. `@/components/ui` → `src/components/ui/`).

**"`components.json` not found"**  
shadcn has not been initialized in your project yet. Run `pnpm dlx shadcn@latest init` and follow the prompts, then add the `registries` field manually as shown in [Step 1](#33-step-1--configure-your-project).

**"pnpm: command not found"**  
Install pnpm first: `npm install -g pnpm`.

---

## 4. For Maintainers

This section is for developers who manage what is inside the registry — adding new components, updating existing ones, and keeping the registry healthy.

### 4.1 Prerequisites

- **pnpm** v11.10.0 or later
- **Node.js** v24.12.0 or later
- Access to the registry repository

---

### 4.2 Repository structure

```
matrix-registry/
│
├── components/
│   └── ui/                        ← Vendored shadcn primitives live here
│       ├── button.tsx             ← Copied from shadcn, reviewed and owned
│       ├── badge.tsx
│       ├── separator.tsx
│       └── ...
│
├── registry/
│   └── blocks/                    ← Matrix-authored blocks and compositions
│       └── dashboard-01/
│           └── page.tsx           ← Block page files
│
├── registry.json                  ← THE source of truth — declare every item here
│
├── public/
│   └── r/                        ← ⚠ GENERATED — never edit these files manually
│       ├── registry.json          ← Built from root registry.json
│       ├── button.json
│       ├── trust-blue.json
│       └── ...
│
├── components.json                ← Registry's own shadcn config (new-york style, app/globals.css)
└── package.json                   ← Registry scripts live here
```

**The golden rule:** `registry.json` is the only file you edit to add or change what the registry exposes. After editing it, run `pnpm registry:build` to regenerate `public/r/`. Never touch `public/r/` directly — it is fully regenerated on every build.

---

### 4.3 Running the registry

```bash
# Start the registry (builds first, then serves on port 3001)
pnpm dev -p 3001

# Build the output files without starting the server
pnpm registry:build

# Type-check the registry project
pnpm exec tsc --noEmit

# Lint the registry project
pnpm lint
```

**What happens when you run `pnpm dev -p 3001`:**

1. `predev` hook fires → runs `pnpm registry:build` (= `shadcn build`)
2. `shadcn build` reads `registry.json`, validates it, and writes one JSON file per item into `public/r/`
3. Next.js development server starts on port 3001
4. The registry is now accessible at `http://localhost:3001/r/<name>.json`

---

### 4.4 How registry.json works

`registry.json` is the single file that defines everything the `@matrix` registry exposes. It looks like this at the top level:

```json
{
  "$schema": "https://ui.shadcn.com/schema/registry.json",
  "name": "matrix",
  "homepage": "https://www.matrixcomsec.com",
  "items": [
    ...each component, theme, font, block declared here...
  ]
}
```

When a consumer runs `pnpm dlx shadcn@latest add @matrix/button`, the CLI:
1. Reads the consumer's `components.json` → finds `@matrix` → resolves to `http://localhost:3001/r/{name}.json`
2. Fetches `http://localhost:3001/r/button.json`
3. Reads which files and npm packages are required
4. Downloads the `.tsx` files from the registry
5. Writes them into the consumer's project at the paths determined by their `aliases`

---

### 4.5 Item types at a glance

| Type | Use for | Where source lives |
|---|---|---|
| `registry:ui` | shadcn UI primitives (button, badge, input…) | `components/ui/` |
| `registry:component` | More complex components with state or logic | `components/` |
| `registry:block` | Full page sections or dashboard blocks | `registry/blocks/` |
| `registry:theme` | CSS theme tokens (colours, typography, shadows) | Declared inline via `cssVars` in `registry.json` |
| `registry:font` | Font configuration | Declared inline via `font` in `registry.json` |
| `registry:hook` | React hooks | `[PLACEHOLDER: add hooks folder path once created]` |
| `registry:page` | Full pages installed into a specific route | `registry/blocks/<block>/` |
| `registry:file` | Any other file (JSON, config, etc.) | Wherever it lives in the repo |

---

### 4.6 registry.json field reference

| Field | Required | Description |
|---|---|---|
| `name` | ✅ | The identifier used in `@matrix/<name>`. Lowercase, hyphenated. |
| `type` | ✅ | The item type — see the table above. |
| `title` | ✅ | Human-readable display name. |
| `description` | Recommended | Shown when consumers run `shadcn view`. Include what it is and what theme it belongs to. |
| `dependencies` | When needed | npm packages added to the consumer's `package.json`. |
| `devDependencies` | When needed | npm dev-dependencies added to the consumer's `package.json`. |
| `registryDependencies` | When needed | Other `@matrix` items that must be installed alongside this one. Always use the full `@matrix/<name>` format. |
| `files` | For most items | List of source files to serve to consumers. |
| `files[].path` | ✅ | Path to the file relative to the **registry root**. |
| `files[].type` | ✅ | The file type — matches the item type (e.g. `registry:ui`, `registry:component`). |
| `files[].target` | Conditionally | Where the file goes in the consumer project. Required for `registry:page` and `registry:file`. Leave empty (`""`) for `registry:ui` and `registry:component` — the CLI will use the consumer's aliases. |
| `cssVars` | For themes | Inline CSS variables — used for the `trust-blue` theme. |
| `font` | For fonts | Inline font configuration — used for `font-geist`, `font-geist-mono`. |
| `meta.links.source` | Required for vendored items | URL of the original upstream file, pinned to a tag or commit. Never use `main`. Documentation only — the CLI does not follow this. |
| `meta.links.docs` | Recommended | Upstream documentation URL. |
| `meta.links.homepage` | Recommended | Upstream homepage. |

---

### 4.7 Add a vendored shadcn component

Use this process when adding a component from shadcn that does not yet exist in the registry.

**Step 1 — Find the upstream source**

Go to the shadcn GitHub and find the built component JSON, pinned to a release tag:

```
https://raw.githubusercontent.com/shadcn-ui/ui/shadcn@4.16.0/apps/v4/public/r/styles/new-york/<name>.json
```

Open this URL. The `files` array tells you which `.tsx` file to copy.

**Step 2 — Copy the source file into the registry**

Copy the `.tsx` source into `components/ui/`. Do not modify it at this stage. If you need to adapt it for Matrix, add a comment at the top explaining what was changed and why.

**Step 3 — Add the entry to registry.json**

Open `registry.json` and add a new item inside the `items` array:

```json
{
  "name": "separator",
  "type": "registry:ui",
  "title": "Separator",
  "description": "Visually or semantically separates content. Curated Matrix alias for @shadcn/separator, themed via trust-blue tokens.",
  "dependencies": [
    "@radix-ui/react-separator"
  ],
  "files": [
    {
      "path": "components/ui/separator.tsx",
      "type": "registry:ui",
      "target": ""
    }
  ],
  "meta": {
    "links": {
      "source": "https://raw.githubusercontent.com/shadcn-ui/ui/shadcn@4.16.0/apps/v4/public/r/styles/new-york/separator.json",
      "docs": "https://ui.shadcn.com/docs/components/separator",
      "homepage": "https://ui.shadcn.com"
    }
  }
}
```

**Step 4 — Build and verify**

```bash
pnpm registry:build

# Then from a consumer project with @matrix configured:
pnpm dlx shadcn@latest add @matrix/separator --dry-run
```

Read the dry-run output. Confirm the right file is listed and the correct npm dependency appears.

---

### 4.8 Add a Matrix custom component

Use this when you are building something new that does not exist in shadcn — a Matrix-specific layout, dashboard block, or branded composition.

**Step 1 — Write the component source**

- Primitive-level components → place in `components/ui/`
- Blocks and compositions → place in `registry/blocks/<your-block-name>/`

**Important:** Inside your component files, always use the consumer aliases for imports — not relative paths from inside the registry. Use `@/components/ui/button` not `../../components/ui/button`.

**Step 2 — Add the entry to registry.json**

```json
{
  "name": "dashboard-shell",
  "type": "registry:block",
  "title": "Dashboard Shell",
  "description": "Matrix-branded dashboard layout with sidebar and header.",
  "registryDependencies": [
    "@matrix/sidebar",
    "@matrix/trust-blue"
  ],
  "files": [
    {
      "path": "registry/blocks/dashboard-shell/page.tsx",
      "type": "registry:page",
      "target": "app/dashboard/page.tsx"
    }
  ],
  "meta": {
    "links": {
      "source": "/r/dashboard-shell.json",
      "docs": "/",
      "homepage": "https://www.matrixcomsec.com"
    }
  }
}
```

**Step 3 — Build and verify**

```bash
pnpm registry:build
pnpm exec tsc --noEmit

# From a consumer project:
pnpm dlx shadcn@latest add @matrix/dashboard-shell --dry-run
```

---

### 4.9 Add an external third-party component

Sometimes you need to pull in a component from an external source (not shadcn) — for example, a community registry or a vendor-specific component library.

**The rule: external source → vendor it first, then serve from the registry.**  
Never reference external URLs directly as the source for consumers. Pull the file in, review it, then serve it from here just like any other component.

**Step 1 — Review the external source**

Before copying anything, review the external component's code carefully. Check for:
- Any dependencies that may introduce security risks
- License compatibility
- Code quality and compatibility with Tailwind CSS v4

**Step 2 — Copy the source file into the registry**

Place the file under an appropriate folder — `components/` for components, `components/ui/` for primitives.

**Step 3 — Add the entry to registry.json**

```json
{
  "name": "assistant-ui-mcp-config",
  "type": "registry:component",
  "title": "Assistant UI MCP Config",
  "description": "Local Matrix copy of assistant-ui MCP server configuration, themed via trust-blue tokens.",
  "dependencies": [
    "@assistant-ui/react-mcp",
    "@assistant-ui/store",
    "lucide-react"
  ],
  "registryDependencies": [
    "@matrix/badge",
    "@matrix/button",
    "@matrix/dialog",
    "@matrix/input",
    "@matrix/label",
    "@matrix/separator"
  ],
  "files": [
    {
      "path": "components/assistant-ui/mcp-config.tsx",
      "type": "registry:component",
      "target": "components/assistant-ui/mcp-config.tsx"
    }
  ],
  "meta": {
    "links": {
      "source": "https://raw.githubusercontent.com/<original-source-url>",
      "docs": "<upstream-docs-url>",
      "homepage": "<upstream-homepage>"
    }
  }
}
```

Record the original source URL in `meta.links.source` so future maintainers know where this came from.

**Step 4 — Build and verify**

```bash
pnpm registry:build
pnpm exec tsc --noEmit
pnpm lint
```

---

### 4.10 Update an existing component

When shadcn releases a new version of a component that is already in the registry:

1. Open the new upstream source URL (e.g. `shadcn@4.17.0` tag instead of `4.16.0`).
2. Compare the new source against the file in `components/ui/<name>.tsx`.
3. Apply only changes that are safe and compatible with trust-blue theme tokens. Do not blindly overwrite.
4. If the upstream added or changed an npm dependency, update the `dependencies` array in `registry.json`.
5. Update `meta.links.source` to point to the new pinned tag URL.
6. Run the verification checklist below.

> **Never auto-update.** Each update is a deliberate, reviewed action. If something breaks after an update, the old source is still in git history.

---

### 4.11 Add a live preview

Previews are only for Matrix-authored items with local source files. Vendored shadcn components already link to their upstream documentation.

**Step 1** — Add the item and its `files` to `registry.json`.

**Step 2** — Register the preview name in `components/previews/index.ts`:

```ts
export const previewNames = [
  "trust-blue",
  "dashboard-shell",
  "matrix-sidebar-07",  // ← add your item name here
] as const;
```

**Step 3** — Create a preview component file at `components/previews/<name>-preview.tsx`.

The registry site renders `app/preview/<name>` in an isolated iframe. The `previewNames` array is required because Next.js must know which modules to compile — `registry.json` alone is not enough for this.

---

### 4.12 Verification checklist

Run these commands before every pull request. All must pass with no errors.

```bash
pnpm registry:build       # registry.json must compile without errors
pnpm exec tsc --noEmit    # TypeScript must be clean
pnpm lint                 # No lint violations
```

Then verify from a consumer project with `@matrix` configured:

```bash
pnpm dlx shadcn@latest add @matrix/<your-item> --dry-run
```

Check the dry-run output for:
- ✅ The correct `.tsx` files are listed
- ✅ Imports inside the component use consumer aliases (`@/components/ui/...`), not registry-relative paths
- ✅ All required npm packages appear under "dependencies"
- ✅ `meta.links.source` is pinned to a specific tag — not `main` or `latest`

---

### 4.13 Release checklist

Before telling any consumer team that a new item is ready to use:

1. `pnpm registry:build` — completes without errors
2. `pnpm exec tsc --noEmit` — no TypeScript errors
3. `pnpm lint` — no lint violations
4. `pnpm dev -p 3001` — registry starts and serves correctly
5. `pnpm exec shadcn list http://localhost:3001/r/registry.json` — new item appears
6. `pnpm dlx shadcn@latest add @matrix/<item> --dry-run` — output looks correct
7. Install the item for real in a sandbox consumer project — verify it renders correctly with the trust-blue theme applied
8. Inform the relevant teams the item is available

---

### 4.14 Troubleshooting — Maintainer

**`registry:build` fails with "file not found"**  
A `files[].path` in `registry.json` points to a file that does not exist yet. Either create the file at that path or correct the path in `registry.json`. The `dashboard-01` block is a known example — `page.tsx` and `data.json` must be added at `registry/blocks/dashboard-01/` before the build will succeed.

**Consumer installs the item but gets the wrong files**  
Check that the file paths in `files[].path` in `registry.json` match where the source files actually live. Run `pnpm registry:build` after any path change.

**Consumer ran the install but the item is not showing up**  
The registry was not rebuilt after you added the item to `registry.json`. Run `pnpm registry:build` then restart the server (`pnpm dev -p 3001`).

**TypeScript errors after adding a new component**  
Run `pnpm exec tsc --noEmit` to find the issue. Common causes: the component imports something that is not in the registry project's dependencies, or an incorrect type is used from a package that is not installed.

**`predev` does not run before `dev`**  
Confirm you are using pnpm — lifecycle hooks (`predev`) are pnpm features. If you are using npm or yarn, you may need to run `pnpm registry:build` manually before `pnpm dev -p 3001`.

**A consumer reports component has wrong styles after updating**  
You likely changed a CSS class or variable name that the component depended on. Check if the trust-blue token names used in the component still match what is defined in the `cssVars.theme` section of the `trust-blue` item in `registry.json`.

---

## 5. Registry policy

| Rule | What it means in practice |
|---|---|
| **Vendor, don't link** | Every component source file must live in this repository. No consumer ever fetches from an external URL at install time. |
| **One namespace** | App teams install from `@matrix` only. Installing directly from shadcn or a community registry without design-system approval is not allowed. |
| **Pin provenance** | Every vendored file must have `meta.links.source` set to an exact upstream commit or tag URL. Never use `main` or `latest` as the reference. |
| **Review before adding** | External or community components must be reviewed for code quality, security, and license compatibility before being added. |
| **Review every update** | Upstream changes are not pulled automatically. Every update is a deliberate, reviewed action. |
| **Custom only when necessary** | If a shadcn primitive covers the need without modification, vendor it as-is. Write custom source only for genuine Matrix-specific requirements. |
| **Never commit `public/r/`** | Generated files are never committed. The server always serves the latest build. |
| **Verification gates are mandatory** | `registry:build` + `tsc --noEmit` + `lint` must all pass before any PR is merged. |

---

## 6. Reference

- **`REGISTRY_MANAGEMENT.md`** — deeper maintainer reference for registry architecture decisions
- [shadcn registry documentation](https://ui.shadcn.com/docs/registry) — schema and CLI reference
- [shadcn `components.json` reference](https://ui.shadcn.com/docs/components-json) — all `components.json` fields explained
- [shadcn registry directory](https://ui.shadcn.com/docs/directory) — index of upstream registries for sourcing new components to vendor
- [shadcn blocks](https://ui.shadcn.com/blocks) — upstream block catalogue (source for vendored blocks like `dashboard-01`)
