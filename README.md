<div align="center">

# ⚡ Coding Sahayi

**The Autonomous, Privacy-First AI IDE Powered by Multi-Agent Swarms & Local Fine-Tuning**

[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![WinUI 3](https://img.shields.io/badge/UI-WinUI%203-0078D4?logo=windows&logoColor=white)](https://learn.microsoft.com/en-us/windows/apps/winui/winui3/)
[![Ollama](https://img.shields.io/badge/Local%20AI-Ollama%20%7C%20LM%20Studio-FF6F00?logo=ollama&logoColor=white)](https://ollama.ai)
[![Soup Fine-Tuning](https://img.shields.io/badge/Fine--Tuning-Soup%20CLI-blueviolet)](https://github.com/MakazhanAlpamys/Soup)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE.txt)

<p align="center">
  <b>Coding Sahayi</b> breaks away from passive autocomplete. It is a full-fledged autonomous C# engineering environment that orchestrates specialized agent swarms, verifies code via bidirectional pseudo-terminals, and continuously fine-tunes local models on your successful patterns.
</p>

[Key Features](#-key-features) • [Architecture](#-architecture) • [Getting Started](#-getting-started) • [Autonomous Swarm](#-multi-agent-swarm) • [Roadmap](#-roadmap)

</div>

---

## ⚡ Key Features

* **🐝 Multi-Agent Swarm Orchestration:** Splits engineering tasks across specialized agents (Architect, Worker, Critic, Scribe) to eliminate monolithic hallucinations.
* **🛡️ Zero-Leak Local AI:** Native support for local model runtimes (Ollama, LM Studio) paired with dynamic multi-provider fallback routing (NVIDIA NIM, DeepSeek, OpenAI).
* **🔄 In-IDE Continuous Fine-Tuning:** Integrates with the [Soup](https://github.com/MakazhanAlpamys/Soup) CLI to fine-tune local models on consumer hardware using layer streaming directly from your SQLite `ProjectKnowledge` base.
* **🧪 Autonomous Test-Driven Repair (TDD):** Runs test suites over interactive Windows ConPTY pseudo-terminals (`Pty.Net`). The Critic parses assertion failures and iterates autonomously until tests pass green.
* **🔍 Visual Diff & Selective Acceptance:** Built-in side-by-side color-coded diff viewer (`DiffPlex`). Inspect, accept, or reject proposed code hunks before changes touch your disk.
* **📊 API Metrics & Audit Dashboard:** Real-time visibility into token throughput, latency percentiles, error rates, and estimated cloud costs.

---

## 🏗️ Architecture

```text
               ┌──────────────────────────────────────────────┐
               │              User Workspace / UI             │
               │         (WinUI 3 + Interactive Diff)         │
               └──────────────────────┬───────────────────────┘
                                      │
                         [Swarm Orchestrator Loop]
                                      │
       ┌───────────────┬──────────────┴───────────────┬───────────────┐
       ▼               ▼                             ▼               ▼
┌──────────────┐┌──────────────┐               ┌──────────────┐┌──────────────┐
│  Architect   ││    Worker    │               │    Critic    ││    Scribe    │
│  (Planner &  ││ (Code Writer │               │ (Linter & PTY││(Documentation│
│  AST Search) ││  & Patcher)  │               │ Test Runner) ││ & Knowledge) │
└──────┬───────┘└──────┬───────┘               └──────┬───────┘└──────┬───────┘
       │               │       [Fail: Re-roll]        │               │
       │               └──────────────────────────────┘               ▼
       │                          [Pass: Verify]             ┌─────────────────┐
       │                                                     │ AppDbContext    │
       ▼                                                     │ (SQLite Memory) │
┌──────────────┐                                             └────────┬────────┘
│ Hybrid Router│                                                      │
│ (Local/Cloud)│                                                      ▼
└──────┬───────┘                                             ┌─────────────────┐
       │                                                     │ Soup CLI        │
       ▼                                                     │ (LoRA Streaming)│
[Ollama / APIs]                                              └─────────────────┘

```

---

## 🚀 Multi-Agent Swarm

| Agent | Role | Tools & Capabilities |
| --- | --- | --- |
| **Architect** | High-level decomposition | Semantic code search, Roslyn AST mapping, dependency graphs |
| **Worker** | Implementation & Patching | Atomic file writing, targeted diff patching |
| **Critic** | Verification & TDD | PTY test execution, Roslyn compiler diagnostic checks |
| **Scribe** | Memory & Documentation | SQLite audit trails, continuous training dataset formatting |

---

## 🛠️ Getting Started

### Prerequisites

* Windows 10/11 (x64)
* [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
* [Ollama](https://ollama.ai) (optional for local models, recommended: `qwen2.5-coder:7b`)
* Python 3.10+ (for Soup fine-tuning pipeline)

### Installation

1. **Clone the Repository:**
```bash
git clone [https://github.com/your-username/CodingSahayi.git](https://github.com/your-username/CodingSahayi.git)
cd CodingSahayi

```


2. **Restore Dependencies & Build:**
```bash
dotnet restore
dotnet build -c Release

```


3. **Configure Local Model Runtime:**
* Start Ollama:
```bash
ollama run qwen2.5-coder:7b

```


* Open `Coding Sahayi` -> **Settings** -> Verify endpoint defaults to `http://localhost:11434/v1`.


4. **Run the IDE:**
```bash
dotnet run --project CodingSahayi.csproj

```



---

## 🤝 Contributing

Contributions are welcome! Check out our [Issues](https://www.google.com/search?q=https://github.com/your-username/CodingSahayi/issues) page for open tasks labeled `good first issue` or `help wanted`.

1. Fork the Project
2. Create your Feature Branch (`git checkout -b feature/AmazingFeature`)
3. Commit your Changes (`git commit -m 'Add some AmazingFeature'`)
4. Push to the Branch (`git push origin feature/AmazingFeature`)
5. Open a Pull Request

---

## 📄 License

Distributed under the MIT License. See `LICENSE.txt` for more information.

</html>

```
