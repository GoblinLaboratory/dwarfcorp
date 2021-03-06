﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;

namespace DwarfCorp.Rail
{
    public enum RailShape
    {
        Flat,
        TopHalfSlope,
        BottomHalfSlope,
        SteepSlope
    }

    public class RailConnection
    {
        public Vector3 Entrance;
        public List<Vector3> Exits;
    }

    public class RailPiece
    {
        public String Name = "";
        public RailShape Shape = RailShape.Flat;
        public Point Tile = Point.Zero;
        public List<List<Vector3>> SplinePoints = new List<List<Vector3>>();
        public List<List<Vector2>> RailSplines = new List<List<Vector2>>();
        public List<CompassConnection> CompassConnections = new List<CompassConnection>();

        public List<RailConnection> Connections;

        public void ComputeConnections()
        {
            Connections = new List<RailConnection>();
            
            foreach (var spline in SplinePoints)
            {
                RecordConnection(Connections, spline[0], spline[spline.Count - 1]);
                RecordConnection(Connections, spline[spline.Count - 1], spline[0]);
            }
        }

        private static void RecordConnection(List<RailConnection> Into, Vector3 Start, Vector3 End)
        {
            var existing = Into.FirstOrDefault(c => c.Entrance == Start);
            if (existing == null)
            {
                existing = new RailConnection
                {
                    Entrance = Start,
                    Exits = new List<Vector3>()
                };

                Into.Add(existing);
            }

            if (!existing.Exits.Contains(End))
                existing.Exits.Add(End);
        }

        public IEnumerable<Tuple<Vector3, Vector3>> EnumerateConnections()
        {
            foreach (var connection in Connections)
                foreach (var endpoint in connection.Exits)
                    yield return Tuple.Create(connection.Entrance, endpoint);
        }
    }
}
