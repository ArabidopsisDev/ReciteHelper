# ReciteHelper User Manual

**Version:** v4

**Last Updated:** 2026-06-28

## Introduction

ReciteHelper is an AI-assisted desktop application for exam preparation, course study, and knowledge organization. It turns study material into a `.rhproj` project containing chapters, knowledge points, multiple question types, and a local knowledge base. Each project can then be used for chapter practice, smart review, imported exam sets, mock exams, and help with incorrect answers.

Question data, learning records, and the knowledge base are organized inside the project directory. The knowledge base uses file storage, so no database or standalone vector service needs to be deployed.

Features marked as "Preview" may still change substantially.

---

## Before You Start

### Use a Model Account (OAuth)

Install Node.js 22.19 or newer, choose “使用已有模型账号授权（OAuth）” in the first-run window, select a provider, and sign in. Follow the browser or device-code instructions, then select a chat model and save. The main screen's “模型服务” button lets you switch models, reauthorize, and sign out.

The list is discovered from pi's OAuth providers. It currently includes Anthropic, OpenAI, OpenAI Codex, GitHub Copilot, Kimi Code, Meta, OpenRouter, Radius, and xAI. OAuth selects the text service; the knowledge base separately needs a Qwen key, an existing OpenRouter key, or hosted embedding access. Chapters and questions can still be generated without embeddings.

Credentials are encrypted with the current Windows user's DPAPI in `%LOCALAPPDATA%/ReciteHelper/pi-oauth.dat` and are never stored in `Config.xml`. See the [OAuth integration guide](pi-oauth.md).

### Configure API Keys

Open `Config.xml` in the application directory. The recommended configuration structure is:

```xml
<Config>
    <Version>2</Version>

    <DeepSeekKey>%Environment.GetEnvironmentVariable("DSAPI")%</DeepSeekKey>
    <QwenKey>%Environment.GetEnvironmentVariable("QWEN_API_KEY")%</QwenKey>
    <MissingStrategy>Ignore</MissingStrategy>

    <OCRAccess></OCRAccess>
    <OCRSecret></OCRSecret>

    <PhonkOptions>
        <EnablePhonk>false</EnablePhonk>
        <WrongCount>3</WrongCount>
    </PhonkOptions>

    <RStandard>45</RStandard>
</Config>
```

- `DeepSeekKey`: used for extraction, generation, chapter organization, and explanations in the direct-access option; it is not needed when OAuth or another text service is selected.
- `QwenKey`: used to generate embeddings and search the project knowledge base. If it is missing or a request fails, generated chapters and questions remain available, but knowledge-base assistance will not be shown.
- `MissingStrategy`: controls recovery when generated knowledge is missing. `Ignore` favors speed; `Replay` retries missing content at the cost of more time and API usage.
- `RStandard`: the similarity threshold used when evaluating short answers. It normally does not need to be changed.
- `PhonkOptions`: Easter egg settings. When `EnablePhonk` is enabled, a special effect is triggered after `WrongCount` consecutive incorrect answers.

Keys may be written directly into their elements, but environment variables are recommended so credentials are not stored as plain text. For example:

```xml
<DeepSeekKey>%Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY")%</DeepSeekKey>
<QwenKey>%Environment.GetEnvironmentVariable("QWEN_API_KEY")%</QwenKey>
```

Never commit API keys to Git or share them with other people.

---

## Classic Review Projects

The classic review project is the primary ReciteHelper project type and provides the data used by smart review, mock exams, and game projects.

On the main screen, select "Create New Project," then choose "Classic Review Project."

![Choose a project type](Resources/09-choose-classic.png)

### Prepare Study Material

The project creation window accepts:

- Text-based PDF files whose text can be selected and copied.
- ReciteHelper merge files with the `.meg` extension.

Image-only and scanned PDFs cannot currently be read directly. For multiple documents or DOCX, PPTX, and TXT sources, use the File Merge tool first to produce a `.meg` file. The merge tool supports DOCX, PPTX, PDF, TXT, and existing `.meg` files.

![Create a classic review project](Resources/01-create-project-main.png)

### Create a Project

Enter a project name, storage directory, and study-material path, then select "Confirm Create." The progress window reports four stages in real time:

1. **Read Text**: reads content from the PDF or `.meg` file.
2. **Extract Knowledge**: sends text chunks to the AI to generate knowledge points plus choice, fill-in-the-blank, term-definition, and essay questions. True/false questions come only from imported exam sets.
3. **Cluster Text**: merges related topics and organizes chapters and question structures.
4. **Generate Vectors**: builds the project-level file knowledge base used for later retrieval.

Creation can take some time depending on material length, network conditions, and API response speed. Keep the network connection available and do not click the create button repeatedly.

A completed project directory normally contains:

- `<project-name>.rhproj`: chapters, questions, progress, and a reference to the knowledge-base file.
- A knowledge-base file loaded together with the project for vector retrieval.

Keep the entire project directory together. Moving only the `.rhproj` file without its knowledge-base file disables knowledge-base features.

### Load a Project

Open a project from the recent-project list, or choose "Load Existing Project" and select a `.rhproj` file. Chapters, questions, learning records, and the knowledge base are loaded together. Older `.rhproj` projects without a knowledge base can still be used for normal practice.

---

## Learning Knowledge Points

Opening a project displays the chapter selection window, including chapter counts, question counts, and mastery for each chapter.

![Chapter selection](Resources/02-choose-chapter.png)

Select "Learn Knowledge Points" to browse clustered chapters and knowledge points. Choose a chapter on the left and then a knowledge point to read its content. Use "Mark as Mastered" to update its status, or clear the check to mark it as not mastered again.

![Knowledge point learning](Resources/03-knowledge-point.png)

---

## Question Practice

Select a chapter containing questions from the chapter selection window to start practicing.

![Question practice](Resources/04-question-exercise.png)

Five question types are supported:

- **Single-choice questions**: select an answer from options A, B, C, and D, then submit it.
- **Fill-in-the-blank questions**: enter each answer on its corresponding underline; every blank must be completed.
- **True/false questions**: choose True or False. These questions are sourced only from imported exam sets.
- **Term-definition questions**: enter the definition; semantic similarity is used for evaluation.
- **Essay questions**: includes short-answer, discussion, analysis, and calculation tasks with a larger response area.

After submission, the result area shows the judgment, the user's answer, and the correct answer. Results are saved in the project and are used to calculate chapter mastery and schedule smart reviews.

### Incorrect-Answer Assistant and Knowledge Base

When an answer is incorrect and the current project has a usable knowledge base, an assistant button appears in the result area. Selecting it opens a side panel that:

1. Searches the knowledge base for the three points most relevant to the question and correct answer.
2. Displays each matched point and its source content.
3. Highlights in green the content that strongly overlaps the question, user answer, or correct answer.
4. Lets you decide whether to ask the AI for more help. Data is sent only after confirmation.
5. Sends the question, user answer, correct answer, and matched knowledge points to DeepSeek, which explains the mistake and the correct reasoning.

The button is hidden when no knowledge base was built or its file is empty or unavailable. Normal practice remains available.

---

## Smart Review

Choose "Smart Review" from the function menu in the chapter selection window. ReciteHelper uses answer history to select the 30 questions currently most useful to review.

Scheduling is driven by the FSRS-6 memory model (difficulty, stability, retrievability). Every question stores a memory stability and a difficulty from which the current recall probability is predicted. Questions whose predicted recall has fallen below 90% are due and are served lowest-recall first, followed by never-practised questions and finally the ones you still remember well. After each answer the quiz window shows the predicted recall before the answer, the new stability and the suggested next review time.

The model ships with default parameters fitted on large public review datasets, so a new project works without any data. Once a project has accumulated more than 800 scored answers, ReciteHelper fits personal parameters from your own history when the quiz window closes (usually about a second, in the background) and keeps them only if they predict better. Histories from projects created by earlier versions are converted automatically on first use.

---

## Mock Exams

Select "Mock Exam" in the chapter selection window to open the exam settings page.

![Exam settings](Resources/08-exam-setting.png)

You can configure the course number, duration, question count, and selection weight for each chapter. Weights do not need to total 100%; ReciteHelper normalizes them automatically. Generated exams use fixed scores: 3 points for choice, 1 per blank, 1 for true/false, 4 for term definitions, and 5 for essays.

The function menu can import PDF, TXT, HTML, or MHTML exam files. DeepSeek separates files containing multiple papers and generates answers and explanations; papers are stored under the project's `exams` directory. Enable "Load Exam Set" in mock-exam settings to use one, which disables automatic paper-generation settings. Imported exam sets keep the original section or question scores when they can be identified, and only fall back to default type scores when the source has no usable score information. The `small_title` and `main_title` fields in each exam-set JSON customize the two printed titles.

Accept the exam rules to begin. Submit the paper after answering to see the score and response statistics.

![Mock exam](Resources/05-simulate-capital.png)

![Exam result](Resources/06-simulate-result.png)

Select "View Answers" to review each response, the correct answer, and any available explanation. The exam report can also be exported as a text file.

![Exam review](Resources/07-simulate-review.png)

---

## Import and Export

Open the function menu in the chapter selection window and select Export. ReciteHelper creates `rh_output.zip` inside the project directory. The archive includes the project file, its manifest, the knowledge-base file when available, and the `exams` directory, making it suitable for backup or sharing.

Answer statuses are cleared only in the exported copy. The active local project is not changed. A recipient can open the archive with the Import function on the main screen.

---

## Game Projects (Preview)

A game project uses a classic review project as its data source. Select an existing `.rhproj` file, and ReciteHelper will ask the AI to generate the chapters, story, and script used by the visual novel.

![Create a game project](Resources/10-create-galgame.png)

After generation, open the original classic review project and select "Run Game" from the function menu in the chapter selection window.

![Run the game](Resources/11-play-galgame.png)

This feature remains in preview. Generation time and output quality depend on the source material and model responses.

---

## Frequently Asked Questions

**Q: Why are there no chapters or questions after project creation?**

A: Confirm that the PDF contains selectable text, then check the DeepSeek key and network connection. Run OCR on scanned PDFs before importing them as readable text material.

**Q: Practice works, but why is there no knowledge-base button?**

A: The button appears only after an incorrect answer and only when the project's knowledge base is usable. Confirm that a Qwen key was configured during project creation and that the knowledge-base file has not been moved, deleted, or emptied.

**Q: Why does knowledge-base search still require an internet connection?**

A: Knowledge-base data is stored in a local file and requires no database deployment. The query must still be converted into an embedding by Qwen, so an internet connection and valid Qwen key are required.

**Q: Can I import multiple documents or non-PDF material?**

A: Yes. Use the File Merge tool to combine DOCX, PPTX, PDF, TXT, or other `.meg` files, then create a project from the generated `.meg` file.

**Q: Does exporting clear my current learning progress?**

A: No. Question statuses are cleared only from the copy inside the export archive. The original project remains unchanged.

**Q: Can older projects still be opened?**

A: Yes. Question types use backward-compatible deserialization. Older projects without a knowledge base can still be studied and practiced, but they do not provide knowledge-base retrieval.

---

## Changelog

### v4 (2026-06-28)

- Added independent generation, loading, and interaction for single-choice and short-answer questions.
- Added a file-based knowledge base that is built, loaded, imported, and exported with each project.
- Added incorrect-answer retrieval, matched-content highlighting, and optional AI explanations.
- Added a four-stage project creation progress window.
- Added personalized smart review and preset exam features.
- Refactored the application into SharedKernel, Core, Application, Infrastructure, and WPF layers.

### v3 (2026-01-14)

- Added custom exam settings.
- Added a replay strategy for long PDF documents.
- Added multi-file merge projects.
- Integrated AquaAvgFramework for game project generation (Preview).

### v2 (2025-11-25)

- Added exam answer review.
- Added knowledge point learning.
- Improved project export and documentation.

### v1 (2025-11-11)

- Added PDF import and automatic knowledge clustering.
- Added mock exams.
- Added similar-answer evaluation.

---

## Contact and Feedback

- Project homepage: [GitHub repository](https://github.com/ArabidopsisDev/ReciteHelper)
- Feedback email: arab@methodbox.top
- User community group: 1053379975
- Open an issue or send an email with suggestions, feature requests, or bug reports.
