# IntelliSense (Code Completion) Workflow

This document details the internal working mechanism of the IntelliSense feature in ScratchpadSharp, from the UI event triggers to the underlying Roslyn API calls.

## 1. Triggering Mechanism (`CodeCompletionHandler.cs`)

The code completion process is initiated by user interactions in the `CodeEditor`.

### Trigger Events
- **Text Input (`OnTextEntered`)**:
  - Triggered when text is entered.
  - Logic in `ShouldTriggerCompletion`:
    - **Always Triggers**: `.`, `<`
    - **Conditional**: Letters, digits, underscores (if entered within 2 seconds of the last change).
    - **Re-trigger**: If the completion window is already open, it only re-triggers on `.` or `<`.
- **Keyboard Shortcut (`HandleKeyDown`)**:
  - **Ctrl+Space**: Manually forces the completion window to open.

### Request Flow
1.  **Debouncing**: When triggered, `ShowCompletionWindowAsync` waits for `150ms` (`CompletionDebounceMs`) to avoid excessive processing during rapid typing.
2.  **Context Collection**:
    - Current Code: `CodeEditor.Document.Text`
    - Caret Position: `CodeEditor.CaretOffset`
    - Config: Usings and NuGet packages from `MainWindowViewModel`.
3.  **Service Call**: Calls `IRoslynCompletionService.GetCompletionsAsync`.

## 2. Roslyn Processing (`RoslynCompletionService.cs`)

The core logic resides in `RoslynCompletionService`.

### Step 1: Workspace Preparation
- **Initialization Check**: Ensures `RoslynWorkspaceService` is ready.
- **Update References**: If NuGet packages are used, references are updated.
- **Update Document**:
  - `RoslynWorkspaceService.Instance.UpdateDocumentAsync(tabId, code, usings)`
  - This updates the in-memory Roslyn workspace with the latest code.
  - *Note*: It handles "hidden" usings (implicit usings defined in config) by prepending them to the generic code.
- **Position Adjustment**:
  - `CalculateAdjustedPosition`: Adjusts the caret position to account for the prepended hidden usings.

### Step 2: Fetching Completions
- **Get Service**: Retrieves the `CompletionService` from the Roslyn `Document`.
- **Determine Trigger**:
  - Checks the character before the caret.
  - Creates a `CompletionTrigger` (e.g., `CompletionTrigger.CreateInsertionTrigger` for `.`, `(`, etc., or `CompletionTrigger.Invoke` for manual request).
- **Roslyn Call**:
  - `await completionService.GetCompletionsAsync(document, adjustedPosition, trigger, ...)`
  - Returns a raw list of Roslyn `CompletionItem`s.

### Step 3: Filtering & Enhancement
1.  **Keyword Filtering**:
    - *Current Implementation*: Filters out items tagged with `WellKnownTags.Keyword`.
2.  **Enhancement (`EnhanceCompletionItems`)**:
    - Converts Roslyn items to `EnhancedCompletionItem`.
    - **Documentation**: Fetches XML documentation using `completionService.GetDescriptionAsync`.
    - **Kind**: Determines if it's a Class, Method, Property, etc., based on tags.
    - **CompletionSpan**: Calculates the correct text range to be replaced by adjusting `item.Span` for hidden usings.
    - **Priority**: Calculates a score based on:
        - Roslyn's `MatchPriority`.
        - Tags (Locals > Properties/Methods > Types).
3.  **Sorting (`ApplyPrioritySort`)**:
    - 1. `IsRecommended` (Roslyn preselection code snippets, etc.)
    - 2. Calculated `Priority` (Descending)
    - 3. `SortText` (Alphabetical)
    - 4. `DisplayText`

### Step 4: Result Return
- Returns a `CompletionResult` containing the `ImmutableArray` of `EnhancedCompletionItem`s.
- Caps results at `MaxCompletionItems` (1000) to ensure UI performance.

## 3. UI Presentation (`CodeCompletionHandler.cs`)

Back on the UI thread:

1.  **Completion Window Creation**:
    - A `CompletionWindow` (from `AvaloniaEdit`) is created.
    - Size constrained (Min: 450x250, Max defaults).
2.  **Data Population**:
    - `EnhancedRoslynCompletionData` objects are created from the result items.
    - These wrap the data for display in the list (Icon, Text, Documentation).
3.  **Filtering Setup**:
    - **Span-Based Offset**: Uses the `CompletionSpan.Start` from the first completion item to determine `StartOffset`.
    - **Fallback**: If the span is invalid, falls back to manual backward search (looking for letter/digit/underscore).
    - This ensures that if you typed `Cons`, the completions filter against `Cons`, even if `Cons` is part of a complex expression.
4.  **Display**:
    - `completionWindow.Show()` displays the popup.

## Summary Diagram

```mermaid
sequenceDiagram
    participant User
    participant Editor as CodeEditor
    participant Handler as CodeCompletionHandler
    participant Service as RoslynCompletionService
    participant Roslyn as Roslyn Workspace

    User->>Editor: Types Character / Ctrl+Space
    Editor->>Handler: OnTextEntered / OnKeyDown
    Handler->>Handler: Debounce (150ms)
    Handler->>Service: GetCompletionsAsync(code, pos)
    
    Service->>Roslyn: UpdateDocumentAsync(code)
    Service->>Roslyn: GetDocument()
    Service->>Roslyn: CompletionService.GetCompletionsAsync()
    Roslyn-->>Service: CompletionItems
    
    Service->>Service: Filter & Enhance (Docs, Priority)
    Service-->>Handler: CompletionResult (EnhancedItems)
    
    Handler->>Editor: Show CompletionWindow
    Editor-->>User: Display List
