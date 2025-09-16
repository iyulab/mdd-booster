# MDD Booster

A powerful code generator that supports M3L (Meta Model Markup Language) with **SQL Server cascade path validation**.

## Features

✨ **NEW in v2.1.0**: **SQL Server Cascade Path Validation**
- Automatically detects multiple cascade paths that cause SQL Server errors
- Provides clear warnings and suggestions for resolution
- Uses M3L syntax (`!` for NO ACTION, `?` for SET NULL)

🚀 **Code Generation Support:**
- Database projects (SQL Server with cascade validation)
- Model projects (C# entities)
- Server projects (ASP.NET Core APIs)

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

### Quick Start (Direct Parameters)

```bash
# Generate SQL database project
mdd --input tables.md --output ./src/MyApp.Database

# Generate with specific builder type
mdd --input tables.md --output ./src/MyApp --builder ModelProject
```

### Advanced Usage (Settings File)

```bash
# Use settings file for complex configurations
mdd --settings settings.json
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

## M3L Syntax

### Basic Table Definition

```markdown
## User
> User account information

- Id: identifier @primary
- Email: string(320) @unique
- Name: string(100)
- CreatedAt: datetime = "@now"
- UpdatedAt?: datetime
```

### Foreign Key References with Cascade Control

```markdown
## Post
- Id: identifier @primary
- Title: string(200)
- AuthorId: identifier @reference(User)     # CASCADE (default)
- CategoryId: identifier @reference(Category)!  # NO ACTION
- TagId?: identifier @reference(Tag)?       # SET NULL
```

### Cascade Syntax

- `@reference(Table)` - CASCADE delete (default)
- `@reference(Table)!` - NO ACTION (prevents cascade)
- `@reference(Table)?` - SET NULL (nullifies on delete)

### Example with Cascade Path Conflict Resolution

❌ **This will cause SQL Server error:**
```markdown
## Follow
- FollowerId: identifier @reference(User)    # CASCADE
- FollowingId: identifier @reference(User)   # CASCADE - CONFLICT!
```

✅ **Corrected version:**
```markdown
## Follow
- FollowerId: identifier @reference(User)    # CASCADE
- FollowingId: identifier @reference(User)!  # NO ACTION - FIXED!
```

### Advanced Features

#### Enums
```markdown
## UserStatus ::enum
- Active: integer = 0
- Inactive: integer = 1
- Suspended: integer = 2
```

#### Indexes and Constraints
```markdown
## User
- Email: string(320)
- Phone?: string(20)
- @unique(Email)
- @index(Email, Phone)
```

#### Composite Keys
```markdown
## UserRole
- UserId: identifier @reference(User)!
- RoleId: identifier @reference(Role)!
- @unique(UserId, RoleId)
- @primary(UserId, RoleId)
```

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

### Complete Example: Social Media Database

```markdown
# Social Media Platform Database

## User
> Platform user accounts
- Id: identifier @primary
- Email: string(320) @unique
- Username: string(50) @unique
- PasswordHash: string(256)
- CreatedAt: datetime = "@now"

## Post
> User posts and content
- Id: identifier @primary
- Title: string(200)
- Content: string(2000)
- AuthorId: identifier @reference(User)
- CreatedAt: datetime = "@now"
- LikeCount: integer = 0

## PostLike
> Post like system
- Id: identifier @primary
- UserId: identifier @reference(User)!  # NO ACTION to prevent cascade conflicts
- PostId: identifier @reference(Post)   # CASCADE when post deleted
- CreatedAt: datetime = "@now"
- @unique(UserId, PostId)

## Follow
> User follow relationships
- Id: identifier @primary
- FollowerId: identifier @reference(User)!   # NO ACTION
- FollowingId: identifier @reference(User)!  # NO ACTION
- CreatedAt: datetime = "@now"
- @unique(FollowerId, FollowingId)

## PostStatus ::enum
- Draft: integer = 0
- Published: integer = 1
- Archived: integer = 2
```

## Contributing

1. Fork the repository
2. Create a feature branch
3. Add tests for new functionality
4. Submit a pull request

## License

MIT License - see LICENSE file for details.

## Changelog

### v2.1.0 (Latest)
- ✅ Added SQL Server cascade path validation
- ✅ Automatic conflict detection and resolution suggestions
- ✅ Enhanced M3L syntax with `!` and `?` modifiers
- ✅ Improved command-line interface with direct parameters
- ✅ Better error messages and warnings

### v2.0.0
- Complete rewrite with modular builder architecture
- Support for multiple project types
- Enhanced M3L parsing engine