﻿// KillVoxelAct.cs
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
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DwarfCorp.GameStates;
using Microsoft.Xna.Framework;

namespace DwarfCorp
{
    /// <summary>
    /// A creature goes to a voxel, and then hits the voxel until it is destroyed.
    /// </summary>
    [Newtonsoft.Json.JsonObject(IsReference = true)]
    public class KillVoxelAct : CompoundCreatureAct
    {
        public VoxelHandle Voxel { get; set; }

        public KillVoxelAct()
        {

        }

        // Todo: This is not used. Task manager already manages and tracks the number of dwarves assigned to the task.
        public IEnumerable<Status> IncrementAssignment( CreatureAI creature, string designation, int amount)
        {
            VoxelHandle vref = creature.Blackboard.GetData<VoxelHandle>(designation);

            if(vref.IsValid)
            {
                var digBuildOrder = creature.Faction.Designations.GetVoxelDesignation(vref, DesignationType.Dig) as BuildOrder;

                if(digBuildOrder != null)
                {
                    digBuildOrder.NumCreaturesAssigned += amount;
                    yield return Status.Success;
                }
                else
                {
                    yield return Status.Fail;
                }
            }
            else
            {
                yield return Status.Fail;
            }
             
        }

        public bool CheckIsDigDesignation(CreatureAI creature, string designation)
        {
            VoxelHandle vref = creature.Blackboard.GetData<VoxelHandle>(designation);

            if (vref.IsValid)
            {
                var digBuildOrder = creature.Faction.Designations.GetVoxelDesignation(vref, DesignationType.Dig) as BuildOrder;

                if (digBuildOrder != null)
                    return true;
                else
                    return false;
            }

            return false;
        }

        public KillVoxelAct(VoxelHandle voxel, CreatureAI creature) :
            base(creature)
        {
            Voxel = voxel;
            Name = "Kill DestinationVoxel " + voxel.WorldPosition;
            Tree = new Sequence(
                new SetBlackboardData<VoxelHandle>(creature, "DigVoxel", voxel),
                new Domain(() => CheckIsDigDesignation(creature, "DigVoxel"),
                new Sequence(
                    new Wrap(() => IncrementAssignment(creature, "DigVoxel", 1)),
                    new GoToVoxelAct(voxel, PlanAct.PlanType.Radius, creature) {Radius = 2.0f},
                    new DigAct(Agent, "DigVoxel"),
                    new ClearBlackboardData(creature, "DigVoxel"))) 
               | new Wrap(() => IncrementAssignment(creature, "DigVoxel", -1)) & false);
        }
    }

}