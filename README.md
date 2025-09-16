# MDD Booster

A powerful code generator that transforms M3L (Meta Model Markup Language) into production-ready code with **SQL Server cascade path validation**.

[M3L Syntax](https://github.com/iyulab/m3l)

## Features

🚀 **Code Generation Support:**
- Database projects (SQL Server with cascade validation)
- Model projects (C# entities)
- Server projects (ASP.NET Core APIs)

📝 **M3L Language**: Clean, markdown-based syntax for defining data models
- Platform-independent model definitions
- Human-readable and AI-friendly format
- Complete documentation available at [M3L Syntax](https://github.com/iyulab/m3l)

## Installation

### Global Tool (Recommended)

```bash
dotnet tool install --global MDD-Booster
```

### Update

```bash
dotnet tool update --global MDD-Booster
```

## Usage

### Quick Start

```bash
# Auto-detect settings.json in current directory
mdd

# Use specific settings file
mdd "./path/to/settings.json"

# Direct parameters for simple cases
mdd --input tables.md --output ./src/MyApp.Database --builder DatabaseProject
```

## Command Line Options

| Option | Description | Example |
|--------|-------------|---------|
| `--input` | Path to M3L input file | `--input tables.md` |
| `--output` | Output directory path | `--output ./src/MyApp.Database` |
| `--builder` | Builder type | `--builder DatabaseProject` |
| `--settings` | Settings JSON file path | `--settings config/settings.json` |

### Builder Types

- **DatabaseProject** (default): Generates SQL Server database schemas with cascade validation
- **ModelProject**: Generates C# entity models
- **ServerProject**: Generates ASP.NET Core API projects

## M3L Syntax Overview

M3L is a markdown-based language for defining data models. Here's a quick example:

```markdown
## User
> User account information

- Id: identifier @primary
- Email: string(320) @unique
- Name: string(100)
- CreatedAt: datetime = "@now"
- UpdatedAt?: datetime
```

### Key Features

- **Clean Syntax**: Human-readable markdown format
- **Cascade Control**: `@reference(Table)!` (NO ACTION), `@reference(Table)?` (SET NULL)
- **Type Safety**: Strong typing with nullable support (`?`)
- **Rich Metadata**: Enums, indexes, constraints, and more

📖 **Complete M3L documentation**: [M3L Syntax](https://github.com/iyulab/m3l)

## Settings File Configuration

Create a `settings.json` file for advanced configurations:

```json
{
  "logging": {
    "verbose": false,
    "logFilePath": "./logs/mdd-booster.log"
  },
  "mddConfigs": [
    {
      "mddPath": "./models/tables.md",
      "builders": [
        {
          "type": "DatabaseProject",
          "config": {
            "projectPath": "./src/MyApp.Database",
            "tablePath": "dbo/Tables_",
            "generateIndividualFiles": true,
            "generateForeignKeys": true,
            "cascadeDelete": true
          }
        },
        {
          "type": "ModelProject",
          "config": {
            "projectPath": "./src/MyApp.Models",
            "generateRepositories": false
          }
        }
      ]
    }
  ]
}
```

## SQL Server Cascade Path Validation

MDD Booster automatically validates cascade paths and warns about potential SQL Server errors:

### Example Validation Output

```
[CASCADE VALIDATION] Multiple CASCADE paths detected to table 'User': Follow.FollowerId, Follow.FollowingId
[CASCADE VALIDATION] Suggestions:
- Keep Follow.FollowerId as CASCADE
- Change Follow.FollowingId to @reference(User)! (NO ACTION)
[CASCADE VALIDATION] Use @reference(Table)! syntax to set NO ACTION cascade behavior
```

### Common Cascade Path Conflicts

1. **Self-referencing many-to-many** (Follow, Friend relationships)
2. **Complex hierarchies** (User → Plan → Series → User)
3. **Multiple user references** (CreatedBy, UpdatedBy, AssignedTo)

## Examples

### Quick Example: Social Media Database

```markdown
## User
- Id: identifier @primary
- Email: string(320) @unique
- Username: string(50) @unique
- CreatedAt: datetime = "@now"

## Post
- Id: identifier @primary
- Title: string(200)
- AuthorId: identifier @reference(User)
- CreatedAt: datetime = "@now"

## Follow
# Prevents cascade conflicts with NO ACTION
- FollowerId: identifier @reference(User)!
- FollowingId: identifier @reference(User)!
- @unique(FollowerId, FollowingId)
```