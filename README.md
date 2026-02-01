# Massive-Nav-DOTS

**High-performance massive-scale pathfinding core for Unity ECS.** Designed to handle **50,000+ agents** simultaneously using A*, Spatial Hashing, and upcoming Flow Fields / HPA* implementations.

## Project Overview
This is a performance-first navigation engine built strictly on the **Unity DOTS** stack.

### Performance Goals
* **High-Efficiency Core**:  Spatial partitioning logic performs at sub-millisecond speeds for 50k agents (logic-only, Burst-optimized).
* **Throughput**: High-frequency path updates across multiple worker threads.
* **Efficiency**: Targeted at maintaining high FPS on mid-range hardware during massive simulations.

### Key Features
* **Data-Oriented Design**: Built entirely using ECS, Job System, and Burst Compiler. [Done]
* **Spatial Partitioning**: Custom Spatial Hash implementation (sub-millisecond for 50k agents). [Planned]
* **Pathfinding**: High-speed A* implementation with Burst-friendly data structures. [Stable]
* **Flow Field Navigation**: Implementation for extreme agent counts. [Planned]
* **HPA* (Hierarchical Pathfinding)**: Advanced optimization for large maps. [Planned]

### Current Tech Stack
* Unity 6000.2.7f+
* Entities (ECS)
* Burst Compiler
* Mathematics

---
## License & Copyright

Copyright (c) 2026 VraKorvis. All rights reserved.

This software and its source code are the intellectual property of the author.
Unauthorized copying, modification, or distribution is strictly prohibited.
For review and educational purposes only.

---

### ART
Ant by Poly by Google [CC-BY] (https://creativecommons.org/licenses/by/3.0/) via Poly Pizza (https://poly.pizza/m/90PJjBye5ZC)

Beetle by Poly by Google [CC-BY] (https://creativecommons.org/licenses/by/3.0/) via Poly Pizza (https://poly.pizza/m/4yufxgZ1QQ2)
