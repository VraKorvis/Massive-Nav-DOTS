# Massive-Nav-DOTS

**High-performance massive-scale pathfinding core for Unity ECS.** Designed to handle **100,000+ agents** simultaneously using A*, Spatial Hashing, and PBD-based collision resolution. Optimized for Unity 6 and the Data-Oriented Technology Stack (DOTS).

---

## 📺 Video Demo (100,000 Agents in Action)

[![100k Agents](https://img.youtube.com/vi/T-0mthncdHc/hqdefault.jpg)](https://www.youtube.com/watch?v=T-0mthncdHc)

*Click the image above to watch the performance stress test on YouTube.*

---

## 🛠 Setup & Launch Instructions

To run the simulation in the Unity Editor, follow these steps:

1. **Open Scene**: Navigate to the project folder and open the `demo_ecs` scene.
2. **Generate World**:
    - Select the **MapGenerator** object in the **Hierarchy**.
    - In the **Inspector**, find the `Map Editor Generator` script component.
    - Click the **Context Menu** (the three vertical dots ⋮ in the top-right corner of the component).
    - Select **Generate World**. This will initialize the grid, obstacles, and navigation data.
3. **Run**: Press the **Play** button. The simulation (100k agents) will start automatically.

---

## Project Overview
This is a performance-first navigation engine built strictly on the **Unity DOTS** stack, focusing on high-density agent simulations.

### Key Features
* **Data-Oriented Design**: Built 100% using ECS, Job System, and Burst Compiler. [Stable]
* **Spatial Partitioning**: Custom Spatial Hash implementation for $O(1)$ neighbor lookups and PBD collision resolution. [Stable]
* **Pathfinding**: High-speed Parallel A* implementation with Burst-friendly data structures. [Stable]
* **Custom Native Containers**: Includes a hand-optimized **NativeBinaryMinHeap** implemented via `UnsafeUtility`. Features $O(\log n)$ push/pop with zero managed overhead and branchless optimization (`math.select`) for Burst. [Stable]
* **PBD Physics**: Position Based Dynamics for smooth agent-to-agent pushing and separation. [Stable]
* **Flow Field Navigation**: Researching potential for large-scale directional grids to further reduce pathfinding overhead in high-density scenarios. [Planned]
* **HPA* (Hierarchical Pathfinding)**: Advanced optimization for large-scale maps. [Planned]

### Performance & Optimization
* **Massive-Scale Parallelism**: Fully Burst-compiled Job System (IJobEntity/IJobChunk) distributing load across all available CPU cores.
* **Zero Managed Allocations**: The core simulation loop runs entirely on unmanaged memory using Native Containers, ensuring no GC spikes.
* **Efficient Memory Layout**: Optimized for cache locality to maximize CPU throughput.
* **Real-time Metrics**: Maintaining ~40+ FPS with 100,000 active agents on Apple M1 Pro (including Editor overhead).

### Current Tech Stack
* **Engine**: Unity 6000.2.7f+
* **Packages**: Entities (ECS), Burst, Mathematics

---

## License & Copyright

Copyright (c) 2026 VraKorvis. All rights reserved.

This software and its source code are the intellectual property of the author.
Unauthorized copying, modification, or distribution is strictly prohibited.
For review and educational purposes only.

---

### Credits & Assets
* **Ant** by Poly by Google [CC-BY] via [Poly Pizza](https://poly.pizza/m/90PJjBye5ZC)
* **Beetle** by Poly by Google [CC-BY] via [Poly Pizza](https://poly.pizza/m/4yufxgZ1QQ2)