# CODEX.md

This file provides guidance to Codex when working with code in this repository.

## Repository Context

This repository contains Revit BIM automation work, including:

- Dynamo graph files (`.dyn`)
- Python scripts used inside Dynamo/Revit
- PowerShell graph generators
- Revit C# add-ins

The user works with Autodesk Revit workflows and is also learning programming through the work done in this repository.

## User Learning Preference

The user is a coding beginner and wants to learn while changes are being made.

When creating or modifying code, explain the work in a beginner-friendly way:

1. State what problem was observed.
2. Explain the cause in plain language.
3. Name the programming concept the user should learn from the issue.
4. Describe what code was changed and why that change fixes the problem.
5. Include clickable file links with line numbers for the important edited code.
6. Show the important edited code snippets in the explanation, and place the related file/line link next to each snippet.
7. Explain what the edited code does, not only that it was changed.
8. When useful, mention how the user could recognize or debug a similar issue next time.

Prefer this teaching-oriented explanation style for both new code and bug fixes, including future Revit add-ins created in this repository.

## Revit Add-in Development Preference

When building Revit add-ins in this repository:

- Prefer a loader DLL plus engine DLL structure when the add-in is expected to be edited repeatedly during development.
- Keep frequently changed UI and business logic in the engine DLL so most changes can be tested without restarting Revit.
- Explain clearly which files require a Revit restart when changed and which files can be hot-reloaded.
- When discussing fixes, include the relevant source file and line links so the user can inspect the exact code.

## Communication Style

- Use plain Korean when explaining programming concepts unless code identifiers require English.
- Keep explanations practical and tied to the current bug or feature.
- Do not assume prior programming knowledge when a concept is important to understanding the change.
- After editing code, always include clickable file links with line numbers in the chat response.
- After editing code, show the important changed code snippets directly in the chat response near the related file links, so the user can see what changed without opening the file first.
