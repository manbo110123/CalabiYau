using CalabiYau.CollisionCore;

namespace CalabiYau.TankCollision
{
    /// <summary>
    /// Versioned collision configuration shared by the SampleScene Unity client and
    /// the standalone authoritative server. The inclined Cube is intentionally treated
    /// as a solid top-down obstacle until the later three-dimensional slope stage.
    /// </summary>
    public static class TrainingCollisionMap2D
    {
        public const string MapId = "sample-scene-training-ground";
        public const int CollisionRevision = 4;
        private static readonly Vec2D[] SafeSpawnRootPositions =
        {
            new Vec2D(0f, 0f),
            new Vec2D(4f, 0f),
            new Vec2D(8f, 0f),
            new Vec2D(12f, 0f),
            new Vec2D(20f, 0f),
            new Vec2D(-4f, 0f),
            new Vec2D(-12f, 0f),
            new Vec2D(-16f, 0f),
            new Vec2D(-20f, 0f)
        };

        public static bool IsCompatible(string mapId, int collisionRevision)
        {
            return mapId == MapId && collisionRevision == CollisionRevision;
        }

        public static TankWorldCollision2D CreateResolver()
        {
            return new TankWorldCollision2D(CreateMap(), CreateTankSettings());
        }

        public static Vec2D GetSafeSpawnRootPosition(int playerId)
        {
            return GetSpawnCandidateRootPosition(playerId, 0);
        }

        public static int SpawnCandidateCount => SafeSpawnRootPositions.Length;

        /// <summary>
        /// Returns a deterministic candidate sequence for one player. GameWorld validates
        /// each candidate against the authoritative map and current player occupancy.
        /// </summary>
        public static Vec2D GetSpawnCandidateRootPosition(int playerId, int candidateOffset)
        {
            if (playerId <= 0)
            {
                throw new System.ArgumentOutOfRangeException(
                    nameof(playerId),
                    "Player ID must be positive.");
            }

            if (candidateOffset < 0 || candidateOffset >= SafeSpawnRootPositions.Length)
            {
                throw new System.ArgumentOutOfRangeException(
                    nameof(candidateOffset),
                    "Spawn candidate offset must address the configured candidate list.");
            }

            int candidateIndex = ((playerId - 1) + candidateOffset) % SafeSpawnRootPositions.Length;
            return SafeSpawnRootPositions[candidateIndex];
        }

        public static TankCollisionMap2D CreateMap()
        {
            // Unity's built-in Plane is 10x10 units and SampleScene scales it by five.
            Aabb2D worldBounds = new Aabb2D(Vec2D.Zero, new Vec2D(25f, 25f));
            StaticCollider2D[] colliders =
            {
                // SampleScene/Cube: vertical static pillar.
                StaticCollider2D.FromAabb(
                    1,
                    new Aabb2D(new Vec2D(-9.3f, 3.57f), new Vec2D(0.5851f, 0.5f))),

                // SampleScene/Cube (1): minimum-area OBB enclosing its pitched/rolled
                // box's X/Z projection. This remains a solid Tank-phase obstacle but
                // removes the large empty corners produced by the previous AABB.
                StaticCollider2D.FromGameplayYaw(
                    2,
                    new Vec2D(4.63f, 7.63f),
                    new Vec2D(2.0163f, 1.5487f),
                    1.3668512f),

                // SampleScene/Cube (2): NetworkStaticMapBody2D resets/freezes its
                // authored Rigidbody while connected, matching this fixed authority.
                StaticCollider2D.FromAabb(
                    3,
                    new Aabb2D(new Vec2D(14.53f, -1.61f), new Vec2D(0.5f, 0.5f)))
            };
            return new TankCollisionMap2D(worldBounds, colliders);
        }

        public static TankCollisionSettings2D CreateTankSettings()
        {
            // Matches the enabled Tank Body child BoxCollider and its local root offset.
            // The gun/tower are presentation and do not enlarge the authoritative body.
            return new TankCollisionSettings2D(
                new Vec2D(1.43f, 2.18f),
                0.05f,
                0.05f,
                128,
                8,
                CollisionMath2D.DefaultEpsilon,
                new Vec2D(-1f, 0.92f));
        }
    }
}
