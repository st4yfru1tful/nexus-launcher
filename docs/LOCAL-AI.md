# On-device metadata intelligence

Nexus Launcher 1.0 can create reviewable metadata suggestions on the device
without a Nexus Cloud account. The feature is optional and off by default.

## Prerequisites

Nexus does not bundle or silently install an AI runtime or model. Before using
On-device AI, install [Ollama for Windows](https://ollama.com/download/windows)
from its official source and use Ollama's own documented tools to download at
least one local text-generation model that is appropriate for the computer.

You can verify that Ollama sees an already-downloaded model in a terminal:

~~~powershell
ollama list
~~~

Nexus does not run `ollama pull`, choose a paid/cloud model, accept a remote
Ollama server, or download anything when checking availability. Model storage,
hardware requirements, licenses, and removal remain the user's responsibility
through Ollama.

## Enable and check status

1. Open **Settings** in Nexus.
2. Enable metadata intelligence and select **On-device AI (Ollama)**.
3. Check or refresh the provider status.
4. Select one local library item, choose **AI metadata**, review the proposed
   description and tags, and apply only the changes you want.

The status explains one of these outcomes:

- **Ready** — Nexus found the local runtime and an eligible downloaded model.
- **Runtime unavailable** — a trusted local `ollama.exe` installation was not
  found or its dedicated child process could not start.
- **No local model** — Ollama is available, but Nexus found no eligible model
  that is already downloaded on this device.
- **Unavailable or invalid response** — the local runtime did not return a
  bounded, valid response. No metadata is applied automatically.

Restart or refresh the status after installing the runtime or adding/removing a
local model. Nexus does not switch to Nexus Cloud when the local provider is
unavailable.

## What Nexus starts

Nexus does not attach to an arbitrary server. It starts a dedicated child
process by invoking the resolved local `ollama.exe` directly with `ollama
serve`, without a shell. For that process Nexus:

- binds `OLLAMA_HOST` to a randomly selected `127.0.0.1` port;
- sets `OLLAMA_NO_CLOUD=1` and rejects cloud-model names;
- disables proxy use for the Nexus-to-Ollama connection;
- sets model keep-alive to zero so the loaded model is not kept resident;
- uses only the local runtime-version, model-list, model-details, and
  generation APIs;
- supplies no browser, network, file, command, or other tool capability; and
- stops the child process when the provider is disposed or Nexus closes.

Nexus validates the local model list, rejects cloud and embedding-only
candidates, and requires the selected model's local details to advertise text
generation. It caps request and response sizes, asks for schema-constrained
JSON, and validates the generated metadata again before it can be shown. These
controls reduce accidental behavior; they do not turn an untrusted runtime or
model into a security sandbox. Install software and model files only from
sources you trust.

## Data boundary

Each request contains only the selected item's title and any available
provider, publisher, version, executable filename, and parent-folder label.
Nexus does not include the full executable or install path, launch URI,
arguments, complete library, file contents, or binaries. On-device requests
travel only over the dedicated IPv4 loopback connection.

The suggestion cannot launch, install, download, change a launch target, or
silently overwrite metadata. Nexus presents it for review and applies only the
supported descriptive fields after explicit approval.

## Nexus Cloud is separate

The **Nexus Cloud** provider is an optional client for an externally deployed,
developer-owned OAuth gateway. The public build has no hosted gateway or
identity service. On-device AI does not need a Nexus Cloud session, OpenAI API
key, or end-user OpenAI sign-in. See [AI-GATEWAY.md](AI-GATEWAY.md) for the
cloud deployment contract.
