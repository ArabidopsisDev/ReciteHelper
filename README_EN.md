<p align="center">
  <img src="docs/Resources/readme-hero.png" alt="ReciteHelper 5.0 Lotus" width="100%" />
</p>

<p align="center">
  <a href="README.md">简体中文</a> · <strong>English</strong>
</p>

<h1 align="center">ReciteHelper</h1>

<p align="center">
  <strong>Turn course materials into projects you can practise, search, and remember.</strong>
</p>

<p align="center">
  <a href="https://github.com/OpenRecite/ReciteHelper/actions/workflows/dotnet.yml"><img src="https://img.shields.io/github/actions/workflow/status/OpenRecite/ReciteHelper/dotnet.yml?branch=primary&style=flat-square&label=build" alt="Build" /></a>
  <a href="LICENSE"><img src="https://img.shields.io/github/license/OpenRecite/ReciteHelper?style=flat-square" alt="License" /></a>
  <img src="https://img.shields.io/badge/platform-Windows-0078D4?style=flat-square&logo=windows" alt="Windows" />
  <img src="https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square&logo=dotnet" alt=".NET 10" />
  <img src="https://img.shields.io/badge/version-5.0%20Lotus-C9A35B?style=flat-square" alt="5.0 Lotus" />
</p>

<p align="center">
  <a href="https://github.com/OpenRecite/ReciteHelper/releases"><strong>Download ReciteHelper</strong></a>
  · <a href="docs/manual-en.md">User Guide</a>
  · <a href="https://github.com/OpenRecite/ReciteHelper/issues">Report an Issue</a>
</p>

ReciteHelper is an open-source Windows desktop application for exam preparation, course learning, and knowledge organisation. It uses AI to read study materials, extract knowledge, and generate questions. A local knowledge base, automatic grading, mistake explanations, and personalised FSRS-6 scheduling then turn those materials into a complete learning loop.

AI is not an isolated chat box in ReciteHelper. It participates in document analysis, question generation, retrieval, and mistake assistance, while chapters, questions, learning records, and the vector knowledge base stay with the local project.

## 5.0 Lotus

Version 5.0 reorganises the experience around the path from source material to durable knowledge:

- **FSRS-6 review scheduling** predicts recall from difficulty, memory stability, and elapsed time, prioritising questions that are about to be forgotten. Personal parameters can be fitted after enough review history is available.
- **Five question types** provide dedicated interactions and grading for single-choice, fill-in-the-blank, true/false, term-definition, and essay questions.
- **Paper import and an end-to-end exam flow** recognise exam sets from PDF, TXT, HTML, or MHTML and support chapter-weighted generation, timed exams, automatic grading, and mistake review.
- **A project-local knowledge base** retrieves relevant knowledge for each mistake and can request a grounded AI explanation when needed.
- **Flexible model access** supports direct DeepSeek + Qwen access, a single OpenRouter key, a ReciteHelper hosted-service activation code, or native C# model-account OAuth based on pi's protocols.

## Workflow

```text
Import course material → Extract knowledge and generate questions → Practise / take exams
       → Retrieve and explain mistakes → Schedule the next review with FSRS-6
```

## Screenshots

<table>
  <tr>
    <td width="62%">
      <img src="docs/Resources/12-run-quiz.jpg" alt="Quiz and knowledge-base assistant" />
      <br /><strong>Quiz and knowledge-base assistant</strong><br />Practice, automatic grading, relevant knowledge retrieval, and optional AI explanations in one view.
    </td>
    <td width="38%">
      <img src="docs/Resources/08-exam-setting.png" alt="Exam settings" />
      <br /><strong>Exam settings</strong><br />Configure duration, question count, score, and chapter weights.
    </td>
  </tr>
  <tr>
    <td>
      <img src="docs/Resources/03-knowledge-point.png" alt="Knowledge-point study" />
      <br /><strong>Knowledge-point study</strong><br />Browse knowledge by chapter and keep track of mastery.
    </td>
    <td>
      <img src="docs/Resources/07-simulate-review.png" alt="Exam review" />
      <br /><strong>Exam review</strong><br />Review answers and mistakes, then bring selected questions back into the learning project.
    </td>
  </tr>
</table>

## Model Services

When no valid model configuration is found, ReciteHelper opens a service-selection window on startup.

| Option | What you need | Characteristics |
| --- | --- | --- |
| DeepSeek + Qwen | A DeepSeek key and a Qwen key | Direct text-generation and embedding access; usually faster |
| OpenRouter | One OpenRouter key | Simpler setup; chat and embeddings share one gateway, but may be slower |
| Hosted service | A ReciteHelper activation code | No third-party API keys to manage; model access is provided by the server |
| Model-account OAuth | An eligible provider account | Native C# authorization, automatic browser return, model selection, and refresh; embeddings use a separate service |

Choose “使用已有模型账号授权（OAuth）” in the service-selection window, or reopen it using “模型服务” on the main screen. Supported providers are Anthropic, OpenAI, OpenAI Codex, GitHub Copilot, Kimi Code, Meta, OpenRouter, Radius, and xAI. OAuth credentials are encrypted with Windows DPAPI in the current user's local application data; only provider/model IDs are recorded in `Config.xml`. See the [OAuth integration guide](docs/pi-oauth.md).

API keys are stored in the local `Config.xml` by default and may also reference environment variables. Text required by an AI feature is sent to the model provider you select; project files and the vector knowledge base remain local.

## Quick Start

1. Download the latest build from [Releases](https://github.com/OpenRecite/ReciteHelper/releases).
2. Extract it and run `ReciteHelper.exe`.
3. Choose and configure a model service in the first-run window.
4. Import a text-based PDF, or merge DOCX, PPTX, PDF, and TXT material into a `.meg` file, then create a `.rhproj` learning project.

See the [English user guide](docs/manual-en.md) for complete setup and usage instructions.

## Build from Source

Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) and Visual Studio or JetBrains Rider with WPF support. OAuth is implemented natively in C#: neither building nor running it needs Node.js, npm, or a bridge. The MIT notice for the pi-derived protocols and model catalog is included in the published application.

```powershell
git clone --recurse-submodules https://github.com/OpenRecite/ReciteHelper.git
cd ReciteHelper
dotnet restore src/ReciteHelper.slnx
dotnet build src/ReciteHelper.slnx --configuration Release
dotnet run --project ReciteHelper.Wpf/ReciteHelper.Wpf.csproj
```

If the repository was cloned without its submodules, run:

```powershell
git submodule update --init --recursive
```

## Documentation

| Document | Description |
| --- | --- |
| [Chinese user guide](docs/manual-cn.md) | Installation, configuration, and full feature guide |
| [English user guide](docs/manual-en.md) | English setup and usage instructions |
| [OAuth integration guide](docs/pi-oauth.md) | Account authorization, embeddings, implementation, and tests |

## Contributing

Issues, feature proposals, code, tests, and translations are all welcome:

1. Fork the repository and create a branch from `primary`.
2. Follow the existing code style and document behavioural changes.
3. Confirm that the Release build passes before submitting.
4. Open a Pull Request and link the relevant issue or include UI screenshots.

Please read the [Code of Conduct](CODE_OF_CONDUCT.md) before contributing. Use [GitHub Issues](https://github.com/OpenRecite/ReciteHelper/issues) for discussion and bug reports, or email `arab@methodbox.top`. QQ group: `1053379975`.

## License

ReciteHelper is distributed under the [GNU AGPL v3.0 or later](LICENSE). If you distribute a derivative work or provide a network service based on this project, follow the licence's corresponding source requirements.

## Acknowledgements

Thanks to Sati, Mrwhite3142, oife, AcE77505, and every contributor who has helped improve file handling, compatibility, and stability.

<p align="center">
  <img src="docs/Resources/Logos/clublogo.png" alt="Club logo" height="48" />
  &nbsp;&nbsp;&nbsp;
  <img src="docs/Resources/Logos/caylogo.png" alt="CAY logo" height="48" />
</p>

<p align="center">
  If ReciteHelper helps you, consider giving the project a Star.
</p>

[![Star History Chart](https://api.star-history.com/svg?repos=OpenRecite/ReciteHelper&type=date&legend=top-left)](https://www.star-history.com/#OpenRecite/ReciteHelper&type=date)
