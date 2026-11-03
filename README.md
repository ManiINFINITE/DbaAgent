# 🤖 DBA Agent: Autonomous Relational Database Architect

[](https://dotnet.microsoft.com/)
[](https://www.postgresql.org/)
[](https://www.docker.com/)
[](https://spectreconsole.net/)
[](https://shirazu.ac.ir/)

An intelligent, state-driven command-line agent engineered in C# (.NET) that transforms natural language problem briefs into production-grade relational database architectures. The agent conducts iterative technical interviews, benchmarks DBMS engines, generates complete design documentation (with rendered Mermaid ER diagrams), propagates revisions across all artifacts simultaneously, outputs dialect-pure SQL, and automatically validates scripts against a live database instance with self-healing error repair.

Developed as an individual project for the **Database Laboratory** course at **Shiraz University** (Fall 2026).

- **Instructor:** Dr. Farnush Manavi
- **Grader:** Ali Bagherieh

---

## 🏛️ System Architecture

The agent is organized as a deterministic **Finite State Machine (FSM)**. Rather than relying on fragile single-shot prompts, the workflow guides the design process through 6 sequential, durable stages paired with an automated DBMS validation engine:

```text
 ┌─────────────┐     ┌──────────────┐     ┌────────────────┐
 │   Stage 1   │ ──> │   Stage 2    │ ──> │    Stage 3     │
 │   Intake    │     │  Interview   │     │ DBMS Selection │
 └─────────────┘     └──────────────┘     └────────────────┘
   Actors, Entities    7-Category Q&A       Decision Matrix
   & Processes         Stopping Heuristic   PostgreSQL vs All
                            │
                            ▼
 ┌─────────────┐     ┌──────────────┐     ┌────────────────┐
 │   Stage 6   │ <── │   Stage 5    │ <── │    Stage 4     │
 │ SQL + Seed  │     │Revision Loop │     │  Design Docs   │
 └─────────────┘     └──────────────┘     └────────────────┘
  schema.sql (DDL)    Multi-File Impact    conceptual-model.md
  seed.sql (DML)      changelog.md Stamped  logical-schema.md
        │                                  normalization.md
        ▼                                  assumptions.md
 ┌────────────────────────────────────────┐
 │   Bonus (+15%): Real DBMS Validation   │
 │   PostgreSQL (Docker) via Npgsql       │
 │   Self-Healing Error Repair Loop       │
 └────────────────────────────────────────┘
```

---

## ✨ Key Features

- **Decoupled Scriban Prompting:** Zero prompt strings hardcoded in C#. All prompts live in external `.scriban` templates with dynamic directory resolution across environments.
- **Durable Session Hydration:** State is continuously persisted to `output/state.json`. If execution is interrupted, the agent resumes exactly where it left off.
- **Provider-Agnostic LLM Client:** Compatible with GapGPT (Iranian gateway), OpenAI, Azure OpenAI, and local Ollama instances via standard `appsettings.json` configuration without code changes.
- **Atomic Revision Propagation:** Modifying an entity or adding tips in Stage 5 automatically updates `conceptual-model.md`, `logical-schema.md`, `normalization.md`, and `assumptions.md` in unison, logging every change to `changelog.md`.
- **Deterministic Output Sanitization (`TextSanitizer`):** Programmatically strips outermost markdown code fences while preserving internal ```` ```mermaid ```` diagrams and SQL statements.
- **Self-Healing Real DBMS Validation (+15% Bonus):** Executes `schema.sql` and `seed.sql` on a live PostgreSQL Docker container. If a constraint or syntax error occurs, the agent captures the engine error code and prompts the LLM to patch both scripts in an automated retry loop.

---

## 📁 Repository Structure

```text
.
├── prompts/                         # External Scriban prompt templates
│   ├── stage1_intake_system.scriban
│   ├── stage1_intake_user.scriban
│   ├── stage2_interview_questions.scriban
│   ├── stage2_interview_evaluate.scriban
│   ├── stage2_synthesize_requirements.scriban
│   ├── stage3_dbms_selection.scriban
│   ├── stage4_conceptual.scriban
│   ├── stage4_logical.scriban
│   ├── stage4_normalization.scriban
│   ├── stage4_assumptions.scriban
│   ├── stage5_impact_analysis.scriban
│   ├── stage5_update_file.scriban
│   ├── stage6_schema.scriban
│   ├── stage6_seed.scriban
│   └── stage6_sql_repair.scriban
├── src/
│   ├── DbaAgent.Cli/                # Spectre.Console presentation & entry point
│   │   ├── Program.cs
│   │   └── appsettings.json
│   └── DbaAgent.Core/               # Agent workflow engine & stage services
│       ├── Models/                  # State, DTOs, configs
│       ├── Services/                # LLM client, prompt resolver, sanitizer, DB runner
│       └── Stages/                  # Stages 1 through 6 + ValidationEngine
├── output/                          # Generated project deliverables (auto-created)
│   ├── state.json                   # Serialized FSM state
│   ├── requirements.md              # Stage 2: Formal requirements spec
│   ├── dbms-choice.md               # Stage 3: Engine comparison & justification
│   ├── conceptual-model.md          # Stage 4: ER model & Mermaid diagram
│   ├── logical-schema.md            # Stage 4: Tables, data types, indexes, diagram
│   ├── normalization.md             # Stage 4: FDs, 1NF-BCNF proofs, trade-offs
│   ├── assumptions.md               # Stage 4: Boundaries, soft-deletes, privacy
│   ├── changelog.md                 # Stage 5: Iterative revision audit trail
│   ├── schema.sql                   # Stage 6: Dialect-pure executable DDL
│   └── seed.sql                     # Stage 6: Realistic test fixtures
├── logs/
│   └── llm_calls.jsonl              # Full audit log of all prompts, outputs & attempts
├── docs/
│   ├── architecture_report.html     # Formal 3-page Architecture Report (PDF export)
│   └── critique_report.html         # 15% Critique & Self-Correction Report (PDF export)
└── README.md
```

---

## 🚀 Getting Started

### Prerequisites

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or higher
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (for the live validation loop)
- An active API key (GapGPT, OpenAI, or compatible endpoint)

### 1. Clone the Repository

```bash
git clone https://github.com/your-username/DbaAgent.git
cd DbaAgent
```

### 2. Configure `appsettings.json`

Edit `src/DbaAgent.Cli/appsettings.json`:

```json
{
  "LlmConfig": {
    "Provider": "GapGPT",
    "BaseUrl": "https://api.gapgpt.app/v1",
    "ApiKey": "YOUR_GAPGPT_API_KEY",
    "ModelName": "gpt-4o-mini",
    "Temperature": 0.2,
    "MaxAttempts": 3
  },
  "DbValidationConfig": {
    "PostgresConnectionString": "Host=localhost;Port=5433;Database=dba_agent_test;Username=postgres;Password=postgres;",
    "MaxRepairAttempts": 3
  }
}
```

> **Switching Providers:** To use native OpenAI, set `"BaseUrl": "https://api.openai.com/v1"` and supply your OpenAI key. For local Ollama, set `"BaseUrl": "http://localhost:11434/v1"` and `"ApiKey": "ollama"`.

### 3. Spin Up Live PostgreSQL Test Container

To test against a clean instance and avoid conflicts with local services on port 5432, run PostgreSQL mapped to port `5433`:

```bash
docker run --name pg-test -e POSTGRES_PASSWORD=postgres -e POSTGRES_DB=dba_agent_test -p 5433:5432 -d postgres:16-alpine
```

### 4. Build and Run

```bash
dotnet build
dotnet run --project src/DbaAgent.Cli
```

---

## 🖥️ Example Run Transcript

Below is a transcript demonstrating an end-to-end execution of the agent designing an Online Food Delivery Platform.

```text
    ____    ____       _          _                               _   
   |  _ \  | __ )     / \        / \      __ _    ___   _ __    | |_ 
   | | | | |  _ \    / _ \      / _ \    / _` |  / _ \ | '_ \   | __|
   | |_| | | |_) |  / ___ \    / ___ \  | (_| | |  __/ | | | |  | |_ 
   |____/  |____/  /_/   \_\  /_/   \_\  \__, |  \___| |_| |_|   \__|
                                         |___/                       
Session ID: 568273a9 | Current Stage: Intake

── Stage 1: System Intake ──────────────────────────────────────────
Describe the system you need a database for:
(Paste or type your text. When finished, type END on a new line and press Enter)

I need an online food delivery platform. Customers can browse restaurants, 
view menus with food categories and items, place orders with delivery addresses, 
and pay online. Couriers accept orders and deliver them. Restaurants have opening hours.
END

✔ Analyzing requirements and extracting domain entities...
┌─Intake Domain Summary─────────────────────────────────────────────┐
│ Actors: Customers, Couriers, Restaurants, Payment Gateway         │
│ Entities: Customer, Restaurant, Category, FoodItem, Order, Courier │
│ Processes: Menu Browsing, Order Placement, Dispatch, Payment     │
└───────────────────────────────────────────────────────────────────┘
Stage 1 complete! State saved to output/state.json

── Stage 2: Requirements Interview ─────────────────────────────────
── Interview Round 1 ──
[Agent asks 6 technical questions regarding cardinalities, ACID limits, and soft deletes]
Your Answers: [Submitted detailed technical brief]
✔ Evaluating requirements sufficiency...
Evaluation: Need at least 2 rounds to verify schedule exception logic.

── Interview Round 2 ──
[Agent asks follow-up questions on weekly schedules and fraud constraints]
Your Answers: [Submitted RestaurantScheduleOverride and card tokenization rules]
✔ Evaluating requirements sufficiency...
Evaluation: Agent has determined that sufficient information has been gathered.
✔ Synthesizing formal requirements specification (requirements.md)...
Stage 2 Complete! Generated output/requirements.md

── Stage 3: DBMS Selection ─────────────────────────────────────────
✔ Benchmarking requirements against RDBMS engines...
Selected DBMS: PostgreSQL
┌─DBMS Decision: PostgreSQL────────────────────────────────────────┐
│ Evaluated: PostgreSQL (10/10), SQL Server (8/10), MySQL (7/10),    │
│ SQLite (4/10). PostgreSQL selected for native partial unique      │
│ indexes, TIMESTAMPTZ, and high-concurrency MVCC locking.          │
└───────────────────────────────────────────────────────────────────┘
Stage 3 Complete! Written to output/dbms-choice.md

── Stage 4: Design Documentation ───────────────────────────────────
✔ Authoring conceptual model and Mermaid ER diagram (conceptual-model.md)...
✔ Translating to DBMS-specific logical schema (logical-schema.md)...
✔ Performing 1NF-BCNF normalization & functional dependency analysis (normalization.md)...
✔ Documenting design assumptions and boundary conditions (assumptions.md)...
Stage 4 Complete! Generated 4 design artifacts in output/

── Stage 5: Revision Loop ──────────────────────────────────────────
What would you like to do?
> Request a design modification
Describe the requested design modification:
Add support for customer tipping for couriers. Customers can add an optional 
tip amount to an order, and we need to track whether the tip has been paid out.
END

✔ Propagating revision across affected artifacts...
Revision Applied: Added courier tip tracking
Propagated to files: conceptual-model.md, logical-schema.md, normalization.md, assumptions.md
Entry appended to output/changelog.md

What would you like to do?
> Approve design and proceed to SQL Generation
Design explicitly approved! Advancing to Stage 6 (SQL Generation)...

── Stage 6: SQL Generation ─────────────────────────────────────────
✔ Authoring DDL script (schema.sql) for PostgreSQL...
✔ Synthesizing realistic seed dataset (seed.sql) for PostgreSQL...
Stage 6 Complete! Generated SQL artifacts in output/

── Bonus (+15%): Real DBMS Validation Loop ──────────────────────────
Would you like to run automated live validation against the real DBMS instance? [y/n] (y): y
✔ Executing schema and seed scripts in real DBMS...
SUCCESS: Both schema.sql and seed.sql executed cleanly with 0 errors in the live database!

── Pipeline Execution Completed ────────────────────────────────────
┌─────────────────────┬───────────────────────────────────────────┬────────┐
│ Artifact            │ Description                               │ Status │
├─────────────────────┼───────────────────────────────────────────┼────────┤
│ requirements.md     │ Synthesized requirements specification   │ Ready  │
│ dbms-choice.md      │ Comparison matrix & PostgreSQL justification│ Ready  │
│ conceptual-model.md │ Conceptual entities & Mermaid ER diagram  │ Ready  │
│ logical-schema.md   │ Tables, types, constraints, ER diagram    │ Ready  │
│ normalization.md    │ Functional dependencies & 1NF-BCNF proofs │ Ready  │
│ assumptions.md      │ System boundaries & soft-delete rules     │ Ready  │
│ changelog.md        │ Historical log of all propagated revisions│ Ready  │
│ schema.sql          │ Executable DDL script for PostgreSQL      │ Ready  │
│ seed.sql            │ Realistic test fixtures with zero dummies │ Ready  │
└─────────────────────┴───────────────────────────────────────────┴────────┘
```

---

## 🧪 Testing & Verification Checklist

All deliverables generated by the agent conform to the course quality checklist:

- [x] **Zero SQL Execution Errors:** Both `schema.sql` and `seed.sql` run in sequence on PostgreSQL 16 with 0 errors.
- [x] **Relational Rigor:** Normal form evaluated to at least 3NF; deliberate denormalizations (e.g. `orders.total_amount`) are formally justified.
- [x] **Mermaid Compatibility:** All relationship labels are strictly double-quoted (e.g., `ORDER ||--|| PAYMENT : "corresponds to"`), preventing parser crashes.
- [x] **Realistic Data:** `seed.sql` populates every table with realistic addresses, names, and phone numbers without placeholder strings (`test1`, `dummy`).
- [x] **Advanced Constraints:** Partial unique indexes enforce business rules directly at the storage layer (`UNIQUE (customer_id) WHERE (is_default = TRUE AND is_active = TRUE)`).

---

## 📜 Course Documentation Deliverables

Detailed reports required by the syllabus are available in the repository:

1. **Architecture Report (`docs/architecture_report.html`):** 3-page overview of the state-machine orchestrator, Scriban engine, session persistence, and system limitations.
2. **Critique Report (`docs/critique_report.html`):** 15% evaluation report detailing 5 concrete LLM flaws encountered during development and their architectural fixes.

---

## ⚖️ License & Academic Integrity

This project is submitted as an individual assignment for the Database Laboratory course at Shiraz University. All code, prompt architectures, and documentation were designed and implemented by the student. External libraries and tools are credited in accordance with course policies.
