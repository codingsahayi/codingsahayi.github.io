# Coding Sahayi 

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://opensource.org/licenses/MIT)

Coding Sahayi is a blazing-fast, native Windows AI coding assistant built with WinUI 3 and C#. It connects directly to the NVIDIA NIM API (or any OpenAI-compatible endpoint) to provide autonomous, agentic "vibe coding" capabilities directly on your desktop—without the overhead of Python or Node.js wrappers.

## ✨ Features

*   **Native Windows UI:** Built on WinUI 3 for a sleek, responsive, modern Windows 11 design language.
*   **Autonomous Tool Execution:** The agent can independently run native PowerShell commands, build projects, and test code.
*   **Surgical File Patching:** Reads and edits exact blocks of code instead of rewriting entire files, saving massive amounts of API tokens.
*   **Workspace Exploration:** Recursively scans and searches your directories to find the context it needs without manual hand-holding.
*   **Secure API Storage:** Leverages the native Windows Credential Locker to encrypt and store your API keys safely.

## 🛠 Prerequisites

Before running Coding Sahayi, ensure you have the following installed:
*   Windows 10 or Windows 11
*   [.NET 8.0 SDK](https://dotnet.microsoft.com/download) (or newer)
*   Visual Studio 2022 (with the "Windows application development" workload)
*   An API Key from [NVIDIA NIM](https://build.nvidia.com) (or OpenAI)

## 🚀 Installation

1. Clone the repository:
   ```bash
   git clone [https://github.com/MuhammedShabeer/CodingSahayi.git](https://github.com/MuhammedShabeer/CodingSahayi.git)
