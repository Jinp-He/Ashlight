---
name: datatable-management
description: >-
  Manages Luban DataTable Excel files under DataTables/Datas/. Use when the user
  asks to modify, add, or delete rows/columns in .xlsx data tables, update game
  configuration spreadsheets, or mentions DataTables, Luban tables, CardInfo,
  CharaterInfo, or any table under DataTables/Datas/. Enforces mandatory
  validation via Gen.bat after every change.
---

# DataTable Management

## Scope

This skill covers all `.xlsx` files under `DataTables/Datas/` and its subdirectories.
These are Luban data-source spreadsheets that get compiled into:

- **C# code** → `Assets/Gen/`
- **JSON data** → `Assets/Resources/Config/`

## Project Structure

```
DataTables/
├── gen.bat              # Windows validation/generation script
├── gen.sh               # Unix equivalent (data-only)
├── luban.conf           # Luban project config
├── Defines/             # Schema definitions (XML)
├── Datas/               # Excel data sources (modify these)
│   ├── __tables__.xlsx  # Table registry
│   ├── __beans__.xlsx   # Bean definitions
│   ├── __enums__.xlsx   # Enum definitions
│   └── Character/       # Per-module subdirectories
│       ├── #CardInfo.xlsx
│       └── #CharaterInfo.xlsx
└── Scripts/             # Helper scripts (Python)
```

## Mandatory Workflow

Every time you modify any `.xlsx` file under `DataTables/Datas/`, you **MUST** follow this workflow. No exceptions.

### Step 1: Read Before Edit

Before modifying a spreadsheet, read it first using the spreadsheet skill (openpyxl/pandas) to understand the current structure, columns, and data format.

### Step 2: Edit the Spreadsheet

Use `openpyxl` or `pandas` to make the required changes. Follow the spreadsheet skill conventions for formatting and formula handling.

### Step 3: Run Validation (MANDATORY)

After saving changes, **immediately** run the Luban generation command to validate:

```bash
cd DataTables && dotnet ../Tools/Luban/Luban.dll -t all -d json -c cs-simple-json --conf ./luban.conf -x outputCodeDir=../Assets/Gen -x outputDataDir=../Assets/Resources/Config
```

> **Do NOT run `gen.bat` directly** — it contains `pause` which blocks the terminal.
> Use the `dotnet` command above instead.

### Step 4: Check Result

- **If the command exits with code 0**: Changes are valid. Proceed normally.
- **If the command exits with a non-zero code**: Changes are **INVALID**. You **MUST**:
  1. Read the error output carefully
  2. Identify which cells/rows/columns caused the error
  3. Fix the spreadsheet
  4. Re-run validation (repeat from Step 3)
  5. **Do NOT tell the user the task is complete until validation passes**

## Validation Rules

- The generation command validates schema compliance, data types, references, and required fields
- ALL changes must produce a clean generation with exit code 0
- Partial success is NOT acceptable — treat any error as a full failure
- Generated files (`Assets/Gen/*.cs` and `Assets/Resources/Config/*.json`) are expected outputs; do not manually edit them

## Common Error Patterns

| Error | Likely Cause | Fix |
|-------|-------------|-----|
| `type mismatch` | Cell value doesn't match column schema type | Check `__beans__.xlsx` or `Defines/` for expected type |
| `missing field` | Required column is empty | Fill in required values |
| `invalid ref` | Foreign key points to nonexistent record | Verify referenced ID exists in the target table |
| `duplicate id` | Two rows share the same primary key | Ensure IDs are unique |

## Important Notes

- Never modify generated files under `Assets/Gen/` or `Assets/Resources/Config/` directly
- Schema changes (adding new columns/tables) may require updating `__tables__.xlsx`, `__beans__.xlsx`, or `Defines/` as well
- When adding a new data table file, register it in `__tables__.xlsx`
- File names under `Datas/` prefixed with `#` are convention for Luban data files
