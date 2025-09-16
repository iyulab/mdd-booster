# Meta Model Markup Language (M3L) Specification

## Table of Contents
1. [Introduction](#1-introduction)
   1. [Core Principles](#11-core-principles)
   2. [Document Structure](#12-document-structure)
   3. [Expression Patterns](#13-expression-patterns)
   4. [Notation Principles](#14-notation-principles)
2. [Basic Syntax](#2-basic-syntax)
   1. [Namespace Definition](#21-namespace-definition)
   2. [Model Definition](#22-model-definition)
   3. [Field Definition](#23-field-definition)
   4. [Data Type Notation](#24-data-type-notation)
   5. [Attribute Notation](#25-attribute-notation)
3. [Special Elements](#3-special-elements)
   1. [Enum Definition](#31-enum-definition)
   2. [Relationship Definition](#32-relationship-definition)
   3. [Index Definition](#33-index-definition)
   4. [Inheritance and Interfaces](#34-inheritance-and-interfaces)
   5. [Metadata Definition](#35-metadata-definition)
4. [Advanced Features](#4-advanced-features)
   1. [Composite Key Definition](#41-composite-key-definition)
   2. [Comments and Documentation](#42-comments-and-documentation)
   3. [Behavior Definition](#43-behavior-definition)
   4. [Calculated Fields](#44-calculated-fields)
   5. [Conditional Fields](#45-conditional-fields)
   6. [Complex Data Structures](#46-complex-data-structures)
   7. [Validation Rules](#47-validation-rules)
   8. [Templates and Generics](#48-templates-and-generics)
5. [External References](#5-external-references)
   1. [Imports](#51-imports)
   2. [External Schema References](#52-external-schema-references)
6. [Versioning and Migration](#6-versioning-and-migration)
   1. [Schema Versioning](#61-schema-versioning)
   2. [Migration Notation](#62-migration-notation)
7. [Complete Examples](#7-complete-examples)
   1. [Content Management Example](#71-content-management-example)
   2. [Order Processing Example](#72-order-processing-example)
8. [Best Practices and Anti-patterns](#8-best-practices-and-anti-patterns)
   1. [Recommended Practices](#81-recommended-practices)
   2. [Anti-patterns to Avoid](#82-anti-patterns-to-avoid)
   3. [Naming Conventions](#83-naming-conventions)
9. [Appendix](#9-appendix)
   1. [Terminology](#91-terminology)
   2. [Mapping to Implementation](#92-mapping-to-implementation)

## 1. Introduction

M3L (Meta Model Markup Language) is a markdown-based language for defining data models and schemas. It is platform-independent, not bound to any specific programming language or database system, and aims to express data structures and relationships clearly and concisely.

### 1.1 Core Principles

- **Conciseness**: Providing maximum expressiveness with minimal syntax
- **Readability**: Easy to understand with a structure close to natural language
- **Platform Independence**: Not bound to any specific programming language or database
- **Flexibility**: Offering various expression styles for different contexts while maintaining consistency
- **Extensibility**: Flexible structure accommodating various data modeling requirements
- **Compatibility**: Compatible with standard markdown for easy rendering in existing tools
- **AI-Friendly**: Structured format that AI agents can easily interpret and process

### 1.2 Document Structure

In M3L, markdown header levels define document structure with specific meanings:

- **# (H1)**: Document title and namespace
  - Example: `# Domain Data Model` or `# Namespace: domain.example`
  - Recommendation: Only one per document

- **## (H2)**: Main element definitions
  - Defines core elements like models, enums, interfaces
  - Example: `## Person` or `## StatusValue ::enum`

- **### (H3)**: Sections within models
  - Divides sections for indexes, behaviors, metadata, etc.
  - Example: `### Indexes`, `### Relations`, `### Metadata`

### 1.3 Expression Patterns

M3L provides multiple ways to express the same concepts:

- **Simple Format**: Concise one-line expressions (recommended)
  - Example: `- name: string(200) @required`

- **Extended Format**: Multi-line expressions for complex definitions
  - Example: Using nested items for validation rules
  ```markdown
  - email
    - type: string(100)
    - unique: true
    - validate: email
  ```

- **Section Format**: Definitions grouped within dedicated sections (for related elements)
  - Example: Using `### Relations` to group all relationships
  ```markdown
  ### Relations
  - >author
    - target: Person
    - from: author_id
  ```

### 1.4 Notation Principles

- **Not Null by Default**: All fields are considered non-null by default
- **Explicit Null Allowance**: Use `?` suffix after the type to allow null values
- **Conciseness**: Use the most concise notation unless special cases require otherwise
- **Meaning-Centered**: M3L focuses on the meaning and relationships of data rather than implementation details
- **Flexibility**: Provides various expression styles to choose the most appropriate for each situation
- **Consistency**: Similar concepts are expressed in similar ways throughout a document
- **Progressive Disclosure**: Simple elements can be expressed simply, complex elements have dedicated constructs

## 2. Basic Syntax

### 2.1 Namespace Definition

Namespaces logically group models and schemas.

```markdown
# Namespace: domain.example
```

Or

```markdown
# Domain Data Model
```

#### 2.1.1 Nested Namespaces

Nested namespaces can be defined using dot notation:

```markdown
# Namespace: domain.example.inventory
```

#### 2.1.2 Multiple Namespaces in One Document

For multiple namespaces in a single document:

```markdown
# Namespace: domain.common

# Namespace: domain.specific
```

### 2.2 Model Definition

Models are the basic units of data structure in M3L.

#### 2.2.1 Basic Model Definition Syntax

```markdown
## ModelName(Label)

Brief description of the model.

- field1: type @attribute
- field2: type @attribute
```

Components:
- **ModelName**: Identifier for the model (required)
- **Label**: Display name for the model (in parentheses, optional)
- **Description**: Description of the model (optional)
- **Fields**: List of fields composing the model (required)

Example:

```markdown
## Product(Product)

A product represents an item available for purchase.

- id: identifier @primary
- name: string(200)
- price: decimal(10, 2)
```

#### 2.2.2 Model Definition with Inheritance

Models can inherit from other models or interfaces:

```markdown
## Product : BaseModel, Searchable

A product represents an item available for purchase.

- name: string(200)
- price: decimal(10, 2)
```

#### 2.2.3 Model Metadata

Model-level metadata can be defined:

```markdown
## Product

- name: string(200)
- price: decimal(10, 2)

### Metadata
- domain: "retail"
- versioning: true
```

#### 2.2.4 Model Visibility and Access Control

Model visibility can be defined:

```markdown
## Product @public

## SystemSettings @private
```

### 2.3 Field Definition

Fields define individual attributes of a model.

#### 2.3.1 Basic Field Format (Recommended)

```markdown
- fieldName: type @attribute @attribute(value)
```

Components:
- **fieldName**: Name of the field used as an identifier
- **type**: Data type of the field (required)
- **attribute**: Additional information starting with @ (optional)

Example:

```markdown
- id: identifier @primary
- name: string(200) @searchable
- price: decimal(10, 2) @min(0)
- category_id: identifier? @reference(Category)
- created_at: timestamp = now()
```

#### 2.3.2 Extended Field Format

Multi-line format for complex field definitions:

```markdown
- fieldName
  - type: type
  - attribute1: value
  - attribute2: true
  - description: "Detailed description"
```

Example:

```markdown
- email
  - type: string(100)
  - unique: true
  - index: true
  - description: "Primary contact email address"
  - validate: email
```

#### 2.3.3 Field Metadata

Field-specific metadata can be added:

```markdown
- title: string(200)
  - metadata:
    - importance: 2.0
    - display_priority: 1
```

### 2.4 Data Type Notation

M3L provides consistent rules for denoting data types:

#### 2.4.1 Basic Type Notation
- Type names are written in lowercase: `string`, `integer`, `boolean`, etc.
- Type parameters are written in parentheses: `string(100)`, `decimal(10,2)`
- Implementation types vary based on the platform or language used

#### 2.4.2 Nullable Types
- Add a question mark (`?`) after the type to indicate nullable types: `string?`, `integer?`
- Fields not explicitly marked are considered required by default

#### 2.4.3 Array Types
- Add square brackets (`[]`) after the type to indicate array types: `string[]`, `integer[]`
- Arrays of nullable items: `string?[]` (array of nullable strings)
- Nullable arrays: `string[]?` (a nullable array of strings)

#### 2.4.4 Common Type Usage Examples
```markdown
- username: string(50)      # Required string with max length 50
- bio: text?                # Nullable text
- tags: string[]            # Array of strings
- amount: decimal(10,2)     # Decimal number with precision 10, scale 2
- is_active: boolean = true # Boolean with default value true
```

### 2.5 Attribute Notation

In M3L, the `@` prefix is used to define attributes of fields or models. These can be interpreted as constraints, validations, behaviors, etc.

#### 2.5.1 Attribute Notation Rules

- All attributes start with the `@` symbol
- Attributes can be placed at the end of field definitions or on separate lines
- Attributes can have values or be used standalone

#### 2.5.2 Attribute Formats

1. **Standalone Attributes**: `@attributeName`
   ```markdown
   - username: string(50) @unique
   ```

2. **Attributes with Values**: `@attributeName(value)`
   ```markdown
   - description: text @description("User profile description")
   ```

3. **Default Value Attributes**: `= value` (recommended) or `@default(value)`
   ```markdown
   - status: string = "active"
   - count: integer @default(0)
   ```

4. **Attribute-Only Lines**:
   ```markdown
   - @meta(version, "1.0")
   ```

#### 2.5.3 Attribute Usage Examples

```markdown
- id: identifier @primary @generated
- username: string(50) @unique @searchable
- age: integer = 18 @min(13) @max(120)
- parent_id: identifier? @reference(Category)
```

#### 2.5.4 Attribute Precedence

When attributes might conflict, the precedence order is:
1. Explicit field-level attributes
2. Inherited attributes
3. Model-level default attributes

```markdown
## ModelWithDefaults
- @default_attribute(visibility, hidden)

- field1: string  # Gets default visibility: hidden
- field2: string @visibility(visible)  # Overrides default
```

### 2.5.5 Advanced Default Value Expressions

Default values can be more than simple constants. M3L supports various forms of default value expressions.

#### 2.5.5.1 System Functions as Default Values

```markdown
- created_at: timestamp = now()
- uuid: string = generate_uuid()
- expiry_date: date = date_add(today(), 1, 'year')
```

#### 2.5.5.2 Computed Values as Defaults

```markdown
- discount_price: decimal(10, 2) = price * 0.9
```

#### 2.5.5.3 Conditional Default Values

```markdown
- status: string = if(is_verified, "active", "pending")
```

#### 2.5.6 Custom Framework Attributes

M3L allows for custom framework-specific attributes using square brackets:

```markdown
- password: string(100) [DataType(DataType.Password)][JsonIgnore]
- created_at: timestamp = now() [Insert("@now")]
```

These custom attributes can be used to provide platform-specific metadata that will be processed by implementation-specific parsers.

## 3. Special Elements

### 3.1 Enum Definition

Enums provide a predefined set of values that can be used for specific fields.

#### 3.1.1 Standalone Enum Definition

```markdown
## EnumName ::enum
- value1: "Description of value1"
- value2: "Description of value2"
- value3: "Description of value3"
```

Example:

```markdown
## UserStatus ::enum
- active: "Account in normal use"
- suspended: "Temporarily suspended account"
- inactive: "Account not used for a long time"
- banned: "Permanently banned account"
```

#### 3.1.2 Enum Definition with Namespace

```markdown
## Namespace.EnumName ::enum
- value1: "Description of value1"
- value2: "Description of value2"
```

Example:

```markdown
## Order.Status ::enum
- pending: "Order has been created"
- processing: "Order is being processed"
- shipped: "Order has been shipped"
- delivered: "Order has been delivered"
- cancelled: "Order has been cancelled"
```

#### 3.1.3 Enum Definition with Value Types

```markdown
## EnumName ::enum
- value1: integer = 1 "Description of value1"
- value2: integer = 2 "Description of value2"
- value3: integer = 3 "Description of value3"
```

Example:

```markdown
## OrderStatus ::enum
- pending: integer = 100 "Pending order"
- processing: integer = 200 "Order being processed"
- completed: integer = 300 "Completed order"
- cancelled: integer = 400 "Cancelled order"
```

#### 3.1.4 Grouping Enum Values

```markdown
## EnumName ::enum
- group1
  - value1: "Description of value1"
  - value2: "Description of value2"
- group2
  - value3: "Description of value3"
  - value4: "Description of value4"
```

Example:

```markdown
## ProductStatus ::enum
- active
  - available: "In stock and available for purchase"
  - backorder: "Can be ordered but shipping delayed"
- inactive
  - discontinued: "No longer produced"
  - coming_soon: "Not yet available for sale"
```

#### 3.1.5 Enum Values with Special Characters

Values with special characters or spaces should be enclosed in quotes:

```markdown
## Grade ::enum
- "A+": "Excellent"
- "A": "Very Good"
- "B+": "Good"
- "Not Yet Evaluated": "Awaiting evaluation"
```

#### 3.1.6 Enum Inheritance

Enums can inherit values from other enums:

```markdown
## BasicStatus ::enum
- active: "Active"
- inactive: "Inactive"

## UserStatus ::enum : BasicStatus
- suspended: "Suspended"
- banned: "Banned"
```

#### 3.1.7 Inline Enum Definition

For enums used only once, they can be defined inline with the field:

```markdown
- status: enum = "active"
  - active: "Active"
  - inactive: "Inactive"
  - suspended: "Suspended"
```

### 3.2 Relationship Definition

Relationships define connections between models.

#### 3.2.1 Field Level Relationships (Recommended for Simple References)

```markdown
- author_id: identifier @reference(Person)          # CASCADE (default)
- blocked_user_id: identifier @reference(User)!    # NO ACTION (prevent deletion)
- reviewed_by_id?: identifier @reference(User)?    # SET NULL (nullable field)
```

#### 3.2.1.1 Cascade Behavior Syntax

M3L supports concise cascade behavior notation using symbol suffixes:

- **Default (CASCADE)**: `@reference(Model)` - Parent deletion cascades to child
- **NO ACTION**: `@reference(Model)!` - Prevents parent deletion if children exist
- **SET NULL**: `@reference(Model)?` - Sets field to NULL when parent is deleted (requires nullable field)

Alternative explicit syntax:
```markdown
- author_id: identifier @reference(Person) @cascade(cascade)
- blocked_user_id: identifier @reference(User) @cascade(no-action)
- reviewed_by_id?: identifier @reference(User) @cascade(set-null)
```

#### 3.2.2 Model Level Relationships (Single Line)

```markdown
- @relation(products, <- Product.category_id) "Products in this category"
- @relation(parent, -> Category, from: parent_id) "Parent category"
```

#### 3.2.3 Relationship Section (Recommended for Multiple Relationships)

```markdown
### Relations
- >author
  - target: Person
  - from: author_id
  - on_delete: restrict
  
- <comments
  - target: Comment.post_id
```

#### 3.2.4 Relationship Types

Relationship Notation:
- `>target` or `-> Target`: "To" relationship (this model references Target)
- `<target` or `<- Target`: "From" relationship (Target references this model)

Cardinality can be specified:
```markdown
- >category: one-to-one
- <posts: one-to-many
- <>tags: many-to-many
```

#### 3.2.5 Relationship Attributes

Relationships can have additional attributes:

```markdown
- >author
  - target: Person
  - from: author_id
  - on_delete: restrict
  - on_update: cascade
  - load: eager
  - order_by: created_at desc
```

### 3.3 Index Definition

Indexes are used to optimize query performance.

#### 3.3.1 Field Level Indexes (Recommended for Single Column Indexes)

```markdown
- customer_id: identifier @reference(Customer) @index
```

#### 3.3.2 Model Level Indexes (Single Line)

```markdown
- @index(customer_id, order_date, name: "customer_orders") "For customer orders"
- @unique(order_id, product_id) "Ensures uniqueness of products per order"
```

#### 3.3.3 Index Section (Recommended for Multiple Indexes)

```markdown
### Indexes
- customer_orders(For customer order lookup)
  - fields: [customer_id, order_date]
  - unique: false

- status_date(For order lookup by status)
  - fields: [status, order_date]
  - unique: false
```

### 3.3.5 Unique Constraints

Unique constraints ensure that individual fields or combinations of fields maintain uniqueness.

#### 3.3.5.1 Single Field Unique Constraints

Defined at the field level using the `@unique` attribute:

```markdown
- username: string(50) @unique
- email: string(100) @unique @index
```

Or using framework-specific notation:

```markdown
- normalized_email: string(256) [UQ]
```

#### 3.3.5.2 Multi-Column Unique Constraints

To ensure the uniqueness of a combination of multiple columns, defined at the model level:

```markdown
- @unique(column1, column2) "Ensures combined uniqueness of these columns"
```

For example:
```markdown
- @unique(tenant_id, username) "Ensures username uniqueness within each tenant"
```

### 3.4 Inheritance and Interfaces

Inheritance provides a mechanism for sharing common attributes between models.

#### 3.4.1 Interface Definition

```markdown
## Timestampable ::interface
- created_at: timestamp = now()
- updated_at: timestamp = now() @on_update(now())
```

With descriptive comment:

```markdown
## Timestampable ::interface # Common timestamp fields
- created_at: timestamp = now()
- updated_at: timestamp = now() @on_update(now())
```

#### 3.4.2 Base Model Definition

```markdown
## BaseModel
- id: identifier @primary @generated
```

#### 3.4.3 Model with Inheritance

```markdown
## Product : BaseModel, Deletable
- name: string(200)
```

#### 3.4.4 Multiple Inheritance

```markdown
## BlogPost : ContentBase, Commentable, Shareable
```

#### 3.4.5 Inheritance Conflict Resolution

When inheriting conflicting fields:

```markdown
## ContentRevision : ContentBase
- updated_at: timestamp @override  # Explicitly overrides the field from base
```

### 3.5 Metadata Definition

Metadata provides additional information about the model itself or implementation details.

#### 3.5.1 Single Line Approach

```markdown
- @meta(version, "1.0") "Schema version"
- @meta(domain, "sales") "Business domain"
```

#### 3.5.2 Metadata Section (Recommended for Multiple Metadata)

```markdown
### Metadata
- version: "1.0"
- domain: "sales"
- validFrom: "2023-01-01"
- owner: "Data Team"
```

## 4. Advanced Features

### 4.1 Composite Key Definition

Composite keys are primary keys composed of two or more fields.

```markdown
## OrderItem
- order_id: identifier @reference(Order) @primary(1)
- product_id: identifier @reference(Product) @primary(2)
- quantity: integer = 1
```

Multi-line format:

```markdown
## OrderItem
### PrimaryKey
- fields: [order_id, product_id]
```

### 4.2 Comments and Documentation

M3L provides two types of comments:

#### 4.2.1 Visible Comments

Documentation using markdown blockquotes (`>`):
```markdown
> This is visible documentation that appears in the rendered output,
> which should be processed by documentation generators.
```

Header comments using `#`:
```markdown
## Timestampable # Timestampable interface
```

Description for fields:
```markdown
- username: string(50) @unique @index
  > Unique identifier used for login
```

#### 4.2.2 Hidden Comments

Developer comments using HTML comment syntax:
```markdown
<!-- This is a hidden comment for development purposes.
     It doesn't appear in rendered markdown and
     should be ignored by the parser. -->
```

### 4.3 Behavior Definition

Behaviors define events and actions associated with a model.

#### 4.3.1 Single Line Approach

```markdown
- @behavior(before_create, generate_id) "Generate ID before creation"
```

#### 4.3.2 Behavior Section (Recommended for Multiple Behaviors)

```markdown
### Behaviors
- before_create
  - action: generate_id
  - condition: always
  
- after_update
  - action: notify_changes
  - condition: status_changed
```

### 4.4 Calculated Fields

Calculated fields derive their values from other fields.

#### 4.4.1 Basic Calculated Fields

```markdown
- full_name: string @computed("first_name + ' ' + last_name")
```

#### 4.4.2 Complex Calculated Fields

```markdown
- total_price: decimal
  - computed: true
  - formula: "quantity * unit_price * (1 - discount_rate)"
  - persist: false
```

### 4.5 Conditional Fields

Fields that exist or are required only under certain conditions.

#### 4.5.1 Basic Conditional Fields

```markdown
- expiry_date: date @if(status == "temporary")
```

#### 4.5.2 Complex Conditional Fields

```markdown
- company_name: string(100)
  - required: "account_type == 'business'"
  - visible: "account_type == 'business'"
```

### 4.6 Complex Data Structures

Defining complex nested data structures.

#### 4.6.1 Object Types

```markdown
- address: object
  - street: string(100)
  - city: string(50)
  - postal_code: string(20)
  - country: string(2)
```

#### 4.6.2 Array of Objects

```markdown
- addresses: object[]
  - type: string  # Each object in the array has these fields
  - street: string
  - city: string
  - country: string
```

#### 4.6.3 Map Types

```markdown
- preferences: map<string, string>
```

With specified structure:

```markdown
- translations: map<string, object>
  - key_format: "[a-z]{2}"  # Language code format
  - value:
    - title: string
    - description: string
```

### 4.7 Validation Rules

Rules for validating field values.

#### 4.7.1 Basic Validation

```markdown
- username: string(50) @validate(pattern("[a-zA-Z0-9_]+"))
```

#### 4.7.2 Multiple Validation Rules

```markdown
- email: string
  - validate:
    - required: true
    - format: email
    - unique: true
```

#### 4.7.3 Cross-Field Validation

```markdown
### Validations
- password_match:
  - rule: "password == password_confirmation"
  - message: "Passwords do not match"
  - trigger: [create, password_update]
```

### 4.8 Templates and Generics

Defining reusable templates with generic parameters.

#### 4.8.1 Generic Types

```markdown
## List<T>
- items: T[]
- count: integer @computed("items.length")
```

#### 4.8.2 Using Generic Types

```markdown
## ProductList : List<Product>
- category: string
```

## 5. External References

### 5.1 Imports

Importing definitions from other M3L files.

```markdown
@import "common/base_models.m3l"
@import "common/interfaces.m3l" as interfaces
```

### 5.2 External Schema References

Referencing external schemas or models.

```markdown
- category_id: identifier @reference(external://taxonomy.Category)
```

## 6. Versioning and Migration

### 6.1 Schema Versioning

Version information for the schema.

```markdown
# Domain Data Model
### Version
- major: 2
- minor: 1
- patch: 3
- date: 2023-10-15
```

### 6.2 Migration Notation

Defining changes between schema versions.

```markdown
### Migration (v1.0 → v2.0)
- changed:
  - Person.email: @unique (added)
  - Content.body: text → rich_text (type change)
- added:
  - Person.phone: string(20)?
- removed:
  - Person.fax
```

## 7. Complete Examples

### 7.1 Content Management Example

```markdown
# Content Management Data Model

## Timestampable ::interface # Common timestamp fields
- created_at: timestamp = now()
- updated_at: timestamp = now() @on_update(now())

## Trackable ::interface # Common tracking fields
- created_by: identifier? @reference(Person)
- updated_by: identifier? @reference(Person)

## BaseModel : Timestampable
- id: identifier @primary @generated

## Person : BaseModel, Trackable
- username: string(50) @unique @searchable
  > Unique identifier used for login
- email: string(100) @unique @searchable
- password: string(100)
- first_name: string(50)
- last_name: string(50)
- bio: text?
- profile_image: string?(255)
- status: enum = "active"
  - active: "Active account"
  - suspended: "Suspended"
  - inactive: "Inactive account"
- last_login: timestamp?
- full_name: string @computed("first_name + ' ' + last_name")

- @index(email, username, name: "login_info")

- @relation(contents, <- Content.author_id) "Content created by person"
- @relation(comments, <- Comment.author_id) "Comments written by person"

## Content : BaseModel, Trackable
- title: string(200) @searchable
- slug: string(200) @unique
  > Content identifier used in URL
- body: text @searchable
- summary: text?
- status: enum = "draft"
  - draft: "Draft"
  - published: "Published"
  - scheduled: "Scheduled"
  - archived: "Archived"
- author_id: identifier @reference(Person) @index
- category_id: identifier? @reference(Category) @index
- view_count: integer = 0
- published_at: timestamp?
- tags: string[]?

- @relation(author, -> Person, from: author_id) "Author of this content"
- @relation(category, -> Category, from: category_id) "Category of this content"
- @relation(comments, <- Comment.content_id) "Comments on this content"

### Metadata
- searchable: true
- archive_after: 730  # Days
```

### 7.2 Order Processing Example

```markdown
# Order Processing Data Model

## Product : BaseModel
- sku: string(50) @unique
- name: string(200)
- description: text
- price: decimal(10, 2) @min(0)
- sale_price: decimal(10, 2)?
- cost: decimal(10, 2)?
- stock_quantity: integer = 0
- category_id: identifier? @reference(Category)
- is_active: boolean = true
- specifications: object
  - weight: decimal(8, 2)?
  - dimensions: object?
    - length: decimal(8, 2)
    - width: decimal(8, 2)
    - height: decimal(8, 2)
  - color: string?
  - material: string?
- images: object[]
  - url: string
  - alt: string
  - is_primary: boolean = false
- tags: string[]?

- @index(name, sku, name: "product_search", fulltext: true)

- @relation(category, -> Category, from: category_id) "Category this product belongs to"
- @relation(order_items, <- OrderItem.product_id) "Order items containing this product"

## Order : BaseModel
- order_number: string(20) @unique
- customer_id: identifier @reference(Customer)
- status: enum = "pending"
  - pending: "Pending payment"
  - paid: "Paid" 
  - processing: "Processing"
  - shipped: "Shipped"
  - delivered: "Delivered" 
  - cancelled: "Cancelled"
- total_amount: decimal(12, 2) @min(0)
- shipping_address: object
  - street: string
  - city: string
  - state: string
  - postal_code: string
  - country: string(2)
- billing_address: object?
  - street: string
  - city: string
  - state: string
  - postal_code: string
  - country: string(2)
- payment_method: string
- notes: text?
- ordered_at: timestamp = now()
- shipped_at: timestamp?

- @relation(customer, -> Customer, from: customer_id) "Customer who placed this order"
- @relation(items, <- OrderItem.order_id) "Items in this order"

## OrderItem
- order_id: identifier @reference(Order) @primary(1)
- product_id: identifier @reference(Product) @primary(2)
- quantity: integer @min(1)
- unit_price: decimal(10, 2)
- discount: decimal(10, 2) = 0
- subtotal: decimal(12, 2) @computed("quantity * unit_price - discount")

- @relation(order, -> Order, from: order_id) "Order containing this item"
- @relation(product, -> Product, from: product_id) "Product in this order item"
```

## 8. M3L Simple Extensions

M3L can be extended with minimal complexity while maintaining readability and focus on table specifications.

### 8.1 Enhanced Documentation

Improved documentation using markdown blockquotes for better readability:

#### 8.1.1 Model Documentation

```markdown
## User
> User account information for the platform
> Supports both Google OAuth and email authentication

- Id: identifier @primary
- Email: string(320) @unique
- Name: string(100)
```

#### 8.1.2 Field Comments

Use inline comments for field descriptions:

```markdown
- Email: string(320) @unique  # Primary contact email
- CreatedAt: datetime = "@now"  # Account creation timestamp
```

### 8.2 Basic Constraints

Simple field-level constraints for common validation needs:

```markdown
## Product
- Price: decimal(10,2) @min(0)  # Must be positive
- Stock: integer @min(0) @max(9999)  # Inventory limits
- Name: string(200) @required  # Cannot be null or empty
```

### 8.3 Advanced Field Types

Support for modern data types:

```markdown
## UserProfile
- Id: identifier @primary
- Settings: json  # JSON configuration data
- Tags: string[]  # Array of strings
- Metadata: object  # Structured data
  - Theme: string
  - Language: string(5)
  - Timezone: string(50)
```

### 8.4 Cascade Behavior for Foreign Keys

M3L provides concise syntax for controlling foreign key cascade behavior:

#### 8.4.1 Symbol-Based Cascade Notation

```markdown
## Block
> User blocking system with cascade protection

- Id: identifier @primary
- BlockerId: identifier @reference(User)!      # NO ACTION - prevents User deletion
- BlockedUserId: identifier @reference(User)!  # NO ACTION - prevents User deletion
- Reason?: string(500)                         # Optional blocking reason
- CreatedAt: datetime = "@now"

## Report
> Content reporting system with mixed cascade behavior

- Id: identifier @primary
- ReporterId: identifier @reference(User)!     # NO ACTION - preserve reporter
- TargetType: ReportTargetType
- TargetId: identifier
- Status: ReportStatus = "Pending"
- ReviewedBy?: identifier @reference(User)?    # SET NULL - clear on reviewer deletion
```

#### 8.4.2 Cascade Behavior Guidelines

**CASCADE (default)**: Use for parent-child relationships where child data becomes meaningless without parent
```markdown
- AuthorId: identifier @reference(User)        # Delete posts when author deleted
- CategoryId: identifier @reference(Category)  # Delete products when category deleted
```

**NO ACTION (!)**: Use for important reference data that should be preserved
```markdown
- CreatedBy: identifier @reference(User)!      # Preserve audit trail
- ModeratorId: identifier @reference(User)!    # Prevent moderator deletion
```

**SET NULL (?)**: Use for optional relationships where record should survive parent deletion
```markdown
- ReviewedBy?: identifier @reference(User)?    # Clear reviewer on deletion
- AssignedTo?: identifier @reference(User)?    # Clear assignment on deletion
```

## 9. Best Practices and Anti-patterns

### 9.1 Recommended Practices

#### 9.1.1 Organization

- Group related models in the same M3L document
- Put commonly used interfaces and base models in separate files
- Organize fields in a logical order (identifiers first, then core data, then metadata)
- Use sections for complex models with many relationships

#### 9.1.2 Documentation

- Include a description for every model
- Document complex fields, particularly those with business rules
- Use comments to explain non-obvious relationships or constraints
- Provide examples for fields with specific formats

#### 9.1.3 Expression Patterns

- Use the simplest expression pattern that adequately captures the metadata
- Use one-line format for simple fields
- Use model-level attributes for relationships and indexes when possible
- Use section format only when needed for groups of related items
- Be consistent in your chosen pattern throughout a document

#### 9.1.4 Extension Usage

- Use storage hints for multi-tier architectures
- Define business constraints close to field definitions

#### 9.1.5 Cascade Behavior Best Practices

- **Default to CASCADE** for true parent-child relationships where child data has no meaning without parent
- **Use NO ACTION (!)** for audit trail preservation, user blocking systems, and critical reference data
- **Use SET NULL (?)** for optional assignments where the record should survive reference deletion
- **Document cascade decisions** with inline comments explaining the business rationale
- **Test cascade scenarios** thoroughly, especially for complex multi-table relationships
- **Avoid cascade conflicts** by using NO ACTION for circular or complex reference patterns

**Anti-patterns to avoid:**
```markdown
# BAD: Multiple CASCADE paths to same table can cause conflicts
- BlockerId: identifier @reference(User)     # CASCADE
- BlockedUserId: identifier @reference(User) # CASCADE - can cause cascade conflicts!

# GOOD: Use NO ACTION for blocking/audit systems
- BlockerId: identifier @reference(User)!     # NO ACTION - preserve blocking data
- BlockedUserId: identifier @reference(User)! # NO ACTION - preserve blocking data
```
- Apply security attributes consistently across models
- Use events sparingly for critical business state changes