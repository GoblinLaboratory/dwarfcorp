// FarmTask.cs
// 
//  Modified MIT License (MIT)
//  
//  Copyright (c) 2015 Completely Fair Games Ltd.
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// The following content pieces are considered PROPRIETARY and may not be used
// in any derivative works, commercial or non commercial, without explicit 
// written permission from Completely Fair Games:
// 
// * Images (sprites, textures, etc.)
// * 3D Models
// * Sound Effects
// * Music
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DwarfCorp
{
    public class TillTask : Task
    {
        public FarmTile FarmToWork { get; set; }

        public TillTask()
        {
            Priority = PriorityType.Low;
            Category = TaskCategory.TillSoil;
        }

        public TillTask(FarmTile farmToWork)
        {
            FarmToWork = farmToWork;
            Name = "Till " + FarmToWork.Voxel.Coordinate;
            Priority = PriorityType.Low;
            AutoRetry = true;
            Category = TaskCategory.TillSoil;
        }

        public override bool ShouldRetry(Creature agent)
        {
            return true;
        }


        public override bool ShouldDelete(Creature agent)
        {
            return IsFeasible(agent) == Feasibility.Infeasible;
        }

        public override void OnAssign(CreatureAI agent)
        {
            if (FarmToWork != null)
                FarmToWork.Farmer = agent;

            base.OnAssign(agent);
        }

        public override void OnUnAssign(CreatureAI agent)
        {
            if (FarmToWork != null && FarmToWork.Farmer == agent)
                FarmToWork.Farmer = null;

            base.OnUnAssign(agent);
        }

        public override Feasibility IsFeasible(Creature agent)
        {
            if (!agent.Stats.IsTaskAllowed(Task.TaskCategory.TillSoil) &&
                !agent.Stats.IsTaskAllowed(Task.TaskCategory.Plant))
                return Feasibility.Infeasible;

            if (agent.AI.Status.IsAsleep)
                return Feasibility.Infeasible;

            if (FarmToWork == null)
                return Feasibility.Infeasible;

            if (FarmToWork.Voxel.Type.Name == "TilledSoil")
                return Feasibility.Infeasible;

            return Feasibility.Feasible;
        }

        public override bool IsComplete()
        {
            return FarmToWork.Voxel.Type.Name == "TilledSoil";
        }

        public override Act CreateScript(Creature agent)
        {
                return new TillAct(agent.AI) { FarmToWork = FarmToWork, Name = "Till " + FarmToWork.Voxel.Coordinate };
        }

        public override float ComputeCost(Creature agent, bool alreadyCheckedFeasible = false)
        {
            if (FarmToWork == null) return float.MaxValue;
            else
            {
                return (FarmToWork.Voxel.WorldPosition - agent.AI.Position).LengthSquared();
            }
        }
    }
}
