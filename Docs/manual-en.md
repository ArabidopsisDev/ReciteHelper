# ReciteHelper User Manual

**Version:** v2

**Last Updated:** 2025.11.15

## Introduction
ReciteHelper is an AI-powered tool designed to help users efficiently memorize and review knowledge content. By providing various study and review methods, this project assists users in managing, reinforcing, and testing their study material. It is especially suitable for exam preparation, memorization, and knowledge point organization. The project supports multiple types of data input and output and can be flexibly customized according to users' needs.

---

## Features

### Create Project

The software organizes review projects by "Subject." You should create a new review project for each different subject.

On the main interface, click on "Create New Project" to start a subject review project. You should prepare a PDF file with the review material for the subject. This PDF should contain selectable text, not images. Support for image-based files (OCR or multimodal methods) will be added in future releases.

![01](Resources/01-create-project-main.png)

Afterward, fill in the course name, project save path, and review material path, and click "Confirm Create." The software will automatically extract the text from the document and perform intelligent analysis and clustering. This process may take some time, so please wait patiently until a prompt appears. Then, select "Load from Existing Project" and choose the path to the newly created project. The project will then appear in the recent projects list and can be opened directly.

### Knowledge Point Learning

After opening the project, a chapter selection window will pop up, displaying all chapters divided from the review materials.

![02](Resources/02-choose-chapter.png)

Click the "Learn Knowledge Points" button at the top right to open the knowledge point learning page. This page lists each cluster-generated chapter and all knowledge points within it. Clicking a chapter name on the left will expand the knowledge point list. Click on a specific knowledge point to view its content. Once you have mastered a knowledge point, you can click "Mark as Mastered" at the top right, and its status will update to "Mastered" with a checkmark. If you forget a knowledge point during review, you can uncheck the mark to set it as "Not Mastered." There may be a flash when switching knowledge points due to text rendering, which is normal.

To better support review, future versions will offer knowledge point revision recommendation and mastery detection based on the Ebbinghaus forgetting curve.

![03](Resources/03-knowledge-point.png)

### Practice Questions

After learning some knowledge points, you can select chapters to practice questions from the chapter selection interface, which opens the question practice page for that chapter.

![04](Resources/04-question-exercise.png)

Currently, fill-in-the-blank questions are supported, with plans to add multiple-choice, explanation, and short-answer questions. Enter your answer in the answer box and click "Submit Answer." The software will determine if the answer is correct and record your progress. Unlike common applications that mark answers wrong for minor differences, our software accepts similar or fuzzy answers using methods such as LCS, correlation coefficient, TF-IDF, cosine similarity, etc. For example, in the figure, the correct answer is "pixel," and "pixel point" is accepted and judged as correct since their meanings are close. Your answer progress is saved, and you can continue answering after exiting the software. Answer data will also be used to calculate mastery and displayed on the chapter selection for reference.

### Mock Exam

To simulate exam conditions, the software provides a mock exam function. In the current version, the mock exam automatically selects 30 random questions from knowledge points, with a 60-minute time limit. Future versions will support custom question types, two-way specification, and more. Click "Mock Exam" at the top right of the chapter selection to enter the exam mode.

![05](Resources/05-simulate-capital.png)

After agreeing to the exam rules, you can start the exam. Upon finishing, click the "Submit" button to see your score and wrong questions.

![06](Resources/06-simulate-result.png)

Click "View Answers" to review all questions, your answers, correct answers, and explanations. You can also choose to retake the exam to check your mastery.

![07](Resources/07-simulate-review.png)

---

## Frequently Asked Questions (FAQ)

**Q: No chapters appear after creating a project. What should I do?**  
A: Please check if the uploaded PDF contains selectable text. Image-only PDFs are not supported in the current version. Use text-based PDF documents.

**Q: How can I review knowledge points marked as mastered?**  
A: On the knowledge point learning page, you can filter to show "All," "Mastered," or "Not Mastered" knowledge points and freely select what to review.

**Q: How do I export my learning progress?**  
A: In "Project Settings," you can export learning data in JSON format for progress management and data analysis.

---

## Changelog

### v2 (2025.11.15)
- Added review answer feature for mock exams
- Added knowledge point learning function
- Added documentation and specifications

(v2 is under active development)

### v1 (2025.11.11)
- Support PDF import and automatic knowledge clustering
- Added mock exam function
- Improved clustering and chapter recognition algorithms
- Supported fuzzy answer matching

---

## Contact Us & Feedback

- Project Homepage: [GitHub Repo](https://github.com/ArabidopsisDev/ReciteHelper)
- Feedback Email: arab@methodbox.top
- User Community Group: 1053379975
- Feel free to open an issue or send an email with suggestions or questions. The development team will respond promptly.