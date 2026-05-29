# Code Change Explanation Guideline

Use this guideline when explaining code changes after editing files. The goal is to help the user quickly understand what each important code line does without adding comments inside the source file.

## Core Principles

- After modifying code, explain what changed in short, practical language.
- Include clickable file links with line numbers for important edits.
- Assume the user may be a beginner, and explain important programming concepts in practical language.
- Do not add explanatory comments directly inside source code unless the user explicitly asks for code comments.
- Instead, add short comment-like explanations in the chat response near the relevant code snippets.
- Avoid large or formal explanation blocks when a short note next to the code is enough.

## How To Explain Code In The Chat Response

When showing an edited code snippet in the response, add short explanatory notes before or after the snippet for these cases:

- The edited code introduces the main flow of a new feature.
- The reason for the code may not be obvious later.
- The code works around WinForms, Revit API, deployment, hot-swap, or other environment-specific behavior.
- The code intentionally ignores, resets, or overrides saved values.
- The code preserves UI state, applies values to multiple selected rows, or handles behavior that depends on user interaction timing.

Preferred response style:

```csharp
exportPathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72F));
```

Comment-like explanation in the chat:

```text
Sets the first column of exportPathPanel to a fixed width of 72px.
```

```csharp
exportPathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
```

Comment-like explanation in the chat:

```text
Makes this column take the remaining available width.
```

Another acceptable compact style:

```csharp
exportPathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72F));  // label column
exportPathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F)); // input column
exportPathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110F)); // button column
```

## Source Code Comment Rule

Do not add comments directly inside `.cs` files by default. Keep the source code clean, and explain intent in the final chat response instead.

Only add comments inside source files when the user explicitly asks for code comments.

## What To Include In The Final Response

After code edits, include the following when useful, but keep it concise:

1. What changed.
2. Important file links with line numbers.
3. Short code snippets.
4. Comment-like explanations for what the code does.
5. Build or test results.
6. For Revit add-ins, mention whether hot-swap is enough or whether Revit must be restarted.
