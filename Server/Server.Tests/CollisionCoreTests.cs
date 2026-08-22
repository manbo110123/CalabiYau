using CalabiYau.CollisionCore;

internal static class CollisionCoreTests
{
    public static void RunAll()
    {
        DotProjectionAndNormalUseClearTwoDimensionalSemantics();
        NearZeroAxesCannotBeNormalizedOrProjected();
        ShapeConstructionRejectsInvalidValuesButAllowsDegenerateGeometry();
        ObbAxesRemainOrthonormalAfterRotation();
        SegmentCreationProducesAUnitRayAndFiniteDistance();
        AabbOverlapCoversSeparationContactAndExchangeSemantics();
        AabbOverlapHandlesContainmentNegativeCoordinatesAndTinyShapes();
        CircleBoxOverlapHandlesTangencyPenetrationAndContainment();
        CircleObbOverlapUsesTheRotatedClosestPoint();
        ProjectionIntervalsUseNormalizedAxes();
        ObbSatFindsASeparatingAxisAndHandlesRotation();
        ObbSatReturnsMinimumTranslationForContactAndContainment();
        ObbSatPreservesExchangeSemanticsAndAabbCompatibility();
        SatToleranceDistinguishesNumericContactFromRealSeparation();
        RaycastAabbReportsEntryPointDistanceAndNormal();
        RaycastHandlesInsideEdgeParallelAndBackwardStarts();
        RaycastCircleUsesSurfaceDistanceAndHandlesTangency();
        RaycastObbUsesRotatedLocalSlabs();
        SegmentRangeAndTinyNegativeCoordinateGeometryRemainFinite();
    }

    private static void DotProjectionAndNormalUseClearTwoDimensionalSemantics()
    {
        Vec2D value = new Vec2D(3f, 4f);
        Vec2D axis = new Vec2D(2f, 0f);

        AssertNear(Vec2D.Dot(value, axis), 6f, "dot product");
        AssertNear(Vec2D.Dot(value, value.PerpendicularLeft), 0f, "perpendicular normal");
        Assert(CollisionMath2D.TryProjectVector(value, axis, out Vec2D projection), "non-zero axis should accept projection");
        AssertVec(projection, new Vec2D(3f, 0f), "projection onto X axis");
    }

    private static void NearZeroAxesCannotBeNormalizedOrProjected()
    {
        Vec2D nearZero = new Vec2D(CollisionMath2D.DefaultEpsilon * 0.5f, 0f);

        Assert(!nearZero.TryNormalize(out _), "near-zero vector must not produce an unstable unit vector");
        Assert(!CollisionMath2D.TryProjectVector(Vec2D.UnitX, nearZero, out _), "near-zero projection axis must be rejected");
    }

    private static void ShapeConstructionRejectsInvalidValuesButAllowsDegenerateGeometry()
    {
        ExpectThrows<ArgumentOutOfRangeException>(
            () => new Aabb2D(Vec2D.Zero, new Vec2D(-1f, 1f)),
            "negative AABB extent");
        ExpectThrows<ArgumentOutOfRangeException>(
            () => new Circle2D(Vec2D.Zero, -1f),
            "negative circle radius");
        ExpectThrows<ArgumentException>(
            () => new Ray2D(Vec2D.Zero, Vec2D.Zero),
            "zero ray direction");

        Aabb2D pointBox = new Aabb2D(new Vec2D(-2f, 3f), Vec2D.Zero);
        Circle2D pointCircle = new Circle2D(Vec2D.Zero, 0f);
        AssertVec(pointBox.Minimum, pointBox.Maximum, "zero-size AABB remains a well-defined point");
        AssertNear(pointCircle.Radius, 0f, "zero-radius circle remains a well-defined point");
    }

    private static void ObbAxesRemainOrthonormalAfterRotation()
    {
        Obb2D box = new Obb2D(Vec2D.Zero, new Vec2D(2f, 1f), (float)Math.PI / 3f);

        AssertNear(box.AxisX.Length, 1f, "OBB X axis length");
        AssertNear(box.AxisY.Length, 1f, "OBB Y axis length");
        AssertNear(Vec2D.Dot(box.AxisX, box.AxisY), 0f, "OBB axes must be perpendicular");
    }

    private static void SegmentCreationProducesAUnitRayAndFiniteDistance()
    {
        Assert(
            Ray2D.TryCreateFromPoints(new Vec2D(-1f, 2f), new Vec2D(2f, 6f), out Ray2D ray, out float distance),
            "distinct segment endpoints should produce a ray");
        AssertNear(ray.Direction.Length, 1f, "segment ray direction");
        AssertNear(distance, 5f, "segment length");
        Assert(
            !Ray2D.TryCreateFromPoints(Vec2D.Zero, Vec2D.Zero, out _, out _),
            "coincident segment endpoints should be rejected");
    }

    private static void AabbOverlapCoversSeparationContactAndExchangeSemantics()
    {
        Aabb2D first = new Aabb2D(Vec2D.Zero, new Vec2D(1f, 1f));
        Aabb2D separated = new Aabb2D(new Vec2D(3f, 0f), new Vec2D(1f, 1f));
        Aabb2D touching = new Aabb2D(new Vec2D(2f, 0f), new Vec2D(1f, 1f));
        Aabb2D overlapping = new Aabb2D(new Vec2D(1.5f, 0f), new Vec2D(1f, 1f));

        Assert(!CollisionQueries2D.Overlap(first, separated).Hit, "separated AABBs must not overlap");

        OverlapResult2D contact = CollisionQueries2D.Overlap(first, touching);
        Assert(contact.Hit, "exact AABB contact counts as a hit");
        AssertNear(contact.PenetrationDepth, 0f, "contact penetration depth");
        AssertVec(contact.Normal, new Vec2D(-1f, 0f), "contact normal moves the first box out of the second");

        OverlapResult2D forward = CollisionQueries2D.Overlap(first, overlapping);
        OverlapResult2D reverse = CollisionQueries2D.Overlap(overlapping, first);
        AssertNear(forward.PenetrationDepth, 0.5f, "AABB overlap depth");
        AssertNear(reverse.PenetrationDepth, forward.PenetrationDepth, "swapped AABB overlap depth");
        AssertVec(reverse.Normal, -forward.Normal, "swapped AABB overlap normal");
    }

    private static void AabbOverlapHandlesContainmentNegativeCoordinatesAndTinyShapes()
    {
        Aabb2D inner = new Aabb2D(new Vec2D(0.25f, 0f), new Vec2D(0.5f, 0.5f));
        Aabb2D outer = new Aabb2D(Vec2D.Zero, new Vec2D(2f, 2f));
        OverlapResult2D containment = CollisionQueries2D.Overlap(inner, outer);

        Assert(containment.Hit, "contained AABB must overlap");
        AssertNear(containment.PenetrationDepth, 2.25f, "containment must return the full exit distance");
        AssertVec(containment.Normal, Vec2D.UnitX, "contained box exits through its nearest face");

        Aabb2D negativeFirst = new Aabb2D(new Vec2D(-5f, -4f), new Vec2D(1f, 1f));
        Aabb2D negativeSecond = new Aabb2D(new Vec2D(-3.5f, -4f), new Vec2D(1f, 1f));
        AssertNear(
            CollisionQueries2D.Overlap(negativeFirst, negativeSecond).PenetrationDepth,
            0.5f,
            "negative-coordinate overlap");

        Aabb2D tinyFirst = new Aabb2D(Vec2D.Zero, new Vec2D(0.0001f, 0.0001f));
        Aabb2D tinySecond = new Aabb2D(new Vec2D(0.00015f, 0f), new Vec2D(0.0001f, 0.0001f));
        AssertNear(
            CollisionQueries2D.Overlap(tinyFirst, tinySecond).PenetrationDepth,
            0.00005f,
            "tiny positive geometry overlap",
            0.000001f);
    }

    private static void CircleBoxOverlapHandlesTangencyPenetrationAndContainment()
    {
        Aabb2D box = new Aabb2D(Vec2D.Zero, new Vec2D(1f, 1f));
        Circle2D tangent = new Circle2D(new Vec2D(2f, 0f), 1f);
        Circle2D overlapping = new Circle2D(new Vec2D(1.5f, 0f), 1f);
        Circle2D inside = new Circle2D(Vec2D.Zero, 0.5f);

        OverlapResult2D tangentResult = CollisionQueries2D.Overlap(tangent, box);
        Assert(tangentResult.Hit, "tangent circle counts as a hit");
        AssertNear(tangentResult.PenetrationDepth, 0f, "circle tangent depth");
        AssertVec(tangentResult.Normal, Vec2D.UnitX, "circle tangent normal");

        OverlapResult2D overlapResult = CollisionQueries2D.Overlap(overlapping, box);
        AssertNear(overlapResult.PenetrationDepth, 0.5f, "circle/AABB overlap depth");

        OverlapResult2D reverse = CollisionQueries2D.Overlap(box, overlapping);
        AssertVec(reverse.Normal, -overlapResult.Normal, "box/circle reverse normal");

        OverlapResult2D insideResult = CollisionQueries2D.Overlap(inside, box);
        AssertNear(insideResult.PenetrationDepth, 1.5f, "inside circle full exit distance");
        AssertVec(insideResult.Normal, Vec2D.UnitX, "symmetric inside circle uses deterministic face");
    }

    private static void CircleObbOverlapUsesTheRotatedClosestPoint()
    {
        Obb2D box = new Obb2D(Vec2D.Zero, new Vec2D(2f, 0.5f), (float)Math.PI / 4f);
        Circle2D tangent = new Circle2D(box.AxisY, 0.5f);
        Circle2D separated = new Circle2D(box.AxisY * 1.1f, 0.5f);

        OverlapResult2D tangentResult = CollisionQueries2D.Overlap(tangent, box);
        Assert(tangentResult.Hit, "circle tangent to rotated OBB must hit");
        AssertNear(tangentResult.PenetrationDepth, 0f, "rotated circle tangent depth");
        AssertVec(tangentResult.Normal, box.AxisY, "rotated closest-point normal");
        Assert(!CollisionQueries2D.Overlap(separated, box).Hit, "circle outside rotated OBB must separate");
    }

    private static void ProjectionIntervalsUseNormalizedAxes()
    {
        Aabb2D box = new Aabb2D(new Vec2D(2f, -1f), new Vec2D(3f, 4f));
        ProjectionInterval2D interval = CollisionQueries2D.Project(box, new Vec2D(2f, 0f));

        AssertNear(interval.Minimum, -1f, "normalized projection minimum");
        AssertNear(interval.Maximum, 5f, "normalized projection maximum");
        ExpectThrows<ArgumentException>(
            () => CollisionQueries2D.Project(box, Vec2D.Zero),
            "zero projection axis");
    }

    private static void ObbSatFindsASeparatingAxisAndHandlesRotation()
    {
        Obb2D first = new Obb2D(Vec2D.Zero, new Vec2D(2f, 1f), DegreesToRadians(30f));
        Obb2D separated = new Obb2D(new Vec2D(6f, 0f), new Vec2D(1f, 1f), DegreesToRadians(-20f));
        Vec2D[] axes = { first.AxisX, first.AxisY, separated.AxisX, separated.AxisY };
        bool foundSeparatingAxis = false;

        for (int index = 0; index < axes.Length; index++)
        {
            ProjectionInterval2D firstProjection = CollisionQueries2D.Project(first, axes[index]);
            ProjectionInterval2D secondProjection = CollisionQueries2D.Project(separated, axes[index]);
            foundSeparatingAxis |= CollisionQueries2D.AreSeparated(firstProjection, secondProjection);
        }

        Assert(foundSeparatingAxis, "SAT diagnostics must expose at least one separating axis");
        Assert(!CollisionQueries2D.Overlap(first, separated).Hit, "separated rotated OBBs must not overlap");

        Obb2D overlapping = new Obb2D(new Vec2D(1.5f, 0f), new Vec2D(1.5f, 0.75f), DegreesToRadians(-20f));
        OverlapResult2D result = CollisionQueries2D.Overlap(first, overlapping);
        Assert(result.Hit, "rotated OBB overlap must be detected");
        Assert(result.PenetrationDepth > 0f, "rotated OBB overlap must have positive depth");
        AssertNear(result.Normal.Length, 1f, "rotated OBB minimum translation normal");
    }

    private static void ObbSatReturnsMinimumTranslationForContactAndContainment()
    {
        Obb2D first = new Obb2D(Vec2D.Zero, new Vec2D(1f, 1f), DegreesToRadians(45f));
        Obb2D touching = new Obb2D(first.AxisX * 2f, new Vec2D(1f, 1f), DegreesToRadians(45f));
        OverlapResult2D contact = CollisionQueries2D.Overlap(first, touching);

        Assert(contact.Hit, "rotated OBB contact counts as a hit");
        AssertNear(contact.PenetrationDepth, 0f, "rotated OBB contact depth");
        AssertVec(contact.Normal, -first.AxisX, "rotated contact normal");

        Obb2D outer = new Obb2D(Vec2D.Zero, new Vec2D(3f, 4f), DegreesToRadians(30f));
        Obb2D inner = new Obb2D(outer.AxisX * 0.5f, new Vec2D(0.5f, 0.5f), DegreesToRadians(30f));
        OverlapResult2D containment = CollisionQueries2D.Overlap(inner, outer);

        Assert(containment.Hit, "contained OBB must overlap");
        AssertNear(containment.PenetrationDepth, 3f, "contained OBB full exit distance");
        AssertVec(containment.Normal, outer.AxisX, "contained OBB nearest exit normal");
    }

    private static void ObbSatPreservesExchangeSemanticsAndAabbCompatibility()
    {
        Obb2D first = new Obb2D(new Vec2D(-2f, -3f), new Vec2D(2f, 0.75f), DegreesToRadians(25f));
        Obb2D second = new Obb2D(new Vec2D(-0.5f, -2.8f), new Vec2D(1f, 1f), DegreesToRadians(-15f));
        OverlapResult2D forward = CollisionQueries2D.Overlap(first, second);
        OverlapResult2D reverse = CollisionQueries2D.Overlap(second, first);

        Assert(forward.Hit && reverse.Hit, "swapped rotated OBBs must both hit");
        AssertNear(reverse.PenetrationDepth, forward.PenetrationDepth, "swapped OBB penetration depth");
        AssertVec(reverse.Normal, -forward.Normal, "swapped OBB normal");

        Aabb2D aabb = new Aabb2D(new Vec2D(3f, 2f), new Vec2D(1f, 1f));
        Obb2D obb = new Obb2D(new Vec2D(4.25f, 2f), new Vec2D(1f, 0.5f), DegreesToRadians(10f));
        OverlapResult2D aabbFirst = CollisionQueries2D.Overlap(aabb, obb);
        OverlapResult2D obbFirst = CollisionQueries2D.Overlap(obb, aabb);

        Assert(aabbFirst.Hit && obbFirst.Hit, "AABB/OBB conversion must preserve overlap");
        AssertNear(aabbFirst.PenetrationDepth, obbFirst.PenetrationDepth, "AABB/OBB reciprocal depth");
        AssertVec(aabbFirst.Normal, -obbFirst.Normal, "AABB/OBB reciprocal normal");
    }

    private static void SatToleranceDistinguishesNumericContactFromRealSeparation()
    {
        float epsilon = CollisionMath2D.DefaultEpsilon;
        Obb2D first = new Obb2D(Vec2D.Zero, new Vec2D(1f, 1f), 0f);
        Obb2D numericContact = new Obb2D(new Vec2D(2f + epsilon * 0.5f, 0f), new Vec2D(1f, 1f), 0f);
        Obb2D separated = new Obb2D(new Vec2D(2f + epsilon * 2f, 0f), new Vec2D(1f, 1f), 0f);

        OverlapResult2D contact = CollisionQueries2D.Overlap(first, numericContact, epsilon);
        Assert(contact.Hit, "sub-epsilon SAT gap is numeric contact");
        AssertNear(contact.PenetrationDepth, 0f, "sub-epsilon SAT gap has zero depth");
        Assert(!CollisionQueries2D.Overlap(first, separated, epsilon).Hit, "gap above epsilon must separate");
    }

    private static void RaycastAabbReportsEntryPointDistanceAndNormal()
    {
        Aabb2D box = new Aabb2D(Vec2D.Zero, new Vec2D(1f, 1f));
        Ray2D ray = new Ray2D(new Vec2D(-3f, 0f), Vec2D.UnitX);
        RaycastResult2D hit = CollisionQueries2D.Raycast(ray, box, 10f);

        Assert(hit.Hit, "external ray should hit AABB");
        Assert(!hit.StartedInside, "external ray must not be marked inside");
        AssertNear(hit.Distance, 2f, "AABB ray entry distance");
        AssertVec(hit.Point, new Vec2D(-1f, 0f), "AABB ray entry point");
        AssertVec(hit.Normal, new Vec2D(-1f, 0f), "AABB ray entry normal");
        Assert(!CollisionQueries2D.Raycast(ray, box, 1.9f).Hit, "finite ray range must stop before the box");
        ExpectThrows<ArgumentOutOfRangeException>(
            () => CollisionQueries2D.Raycast(ray, box, -1f),
            "negative ray range");
        ExpectThrows<ArgumentException>(
            () => CollisionQueries2D.Raycast(default, box, 1f),
            "default ray without a direction");
    }

    private static void RaycastHandlesInsideEdgeParallelAndBackwardStarts()
    {
        Aabb2D box = new Aabb2D(Vec2D.Zero, new Vec2D(1f, 1f));
        RaycastResult2D inside = CollisionQueries2D.Raycast(
            new Ray2D(Vec2D.Zero, Vec2D.UnitY),
            box,
            10f);
        Assert(inside.Hit && inside.StartedInside, "ray starting inside must immediately hit");
        AssertNear(inside.Distance, 0f, "inside ray distance");
        AssertVec(inside.Normal, -Vec2D.UnitY, "inside ray uses conservative opposite-direction normal");

        RaycastResult2D edge = CollisionQueries2D.Raycast(
            new Ray2D(new Vec2D(-1f, 0f), -Vec2D.UnitX),
            box,
            10f);
        Assert(edge.Hit && edge.StartedInside, "ray starting on an edge counts as inside contact");
        AssertNear(edge.Distance, 0f, "edge ray distance");

        Assert(
            !CollisionQueries2D.Raycast(new Ray2D(new Vec2D(-3f, 2f), Vec2D.UnitX), box, 10f).Hit,
            "parallel ray outside the other slab must miss");
        Assert(
            !CollisionQueries2D.Raycast(new Ray2D(new Vec2D(-3f, 0f), -Vec2D.UnitX), box, 10f).Hit,
            "ray pointing away from a box behind it must miss");
    }

    private static void RaycastObbUsesRotatedLocalSlabs()
    {
        Obb2D box = new Obb2D(Vec2D.Zero, new Vec2D(2f, 1f), DegreesToRadians(45f));
        Ray2D ray = new Ray2D(-box.AxisX * 5f, box.AxisX);
        RaycastResult2D hit = CollisionQueries2D.Raycast(ray, box, 10f);

        Assert(hit.Hit, "ray aligned with rotated OBB axis must hit");
        AssertNear(hit.Distance, 3f, "rotated OBB entry distance");
        AssertVec(hit.Point, -box.AxisX * 2f, "rotated OBB entry point");
        AssertVec(hit.Normal, -box.AxisX, "rotated OBB surface normal");
    }

    private static void RaycastCircleUsesSurfaceDistanceAndHandlesTangency()
    {
        Circle2D circle = new Circle2D(new Vec2D(4f, 0f), 1f);
        RaycastResult2D directHit = CollisionQueries2D.Raycast(
            new Ray2D(Vec2D.Zero, Vec2D.UnitX),
            circle,
            10f);

        Assert(directHit.Hit && !directHit.StartedInside, "external ray must hit the circle");
        AssertNear(directHit.Distance, 3f, "circle entry uses surface distance rather than center projection");
        AssertVec(directHit.Point, new Vec2D(3f, 0f), "circle entry point");
        AssertVec(directHit.Normal, -Vec2D.UnitX, "circle entry normal");

        RaycastResult2D tangentHit = CollisionQueries2D.Raycast(
            new Ray2D(new Vec2D(0f, 1f), Vec2D.UnitX),
            circle,
            10f);
        Assert(tangentHit.Hit, "tangent ray counts as contact");
        AssertNear(tangentHit.Distance, 4f, "circle tangent distance");

        RaycastResult2D insideHit = CollisionQueries2D.Raycast(
            new Ray2D(circle.Center, Vec2D.UnitX),
            circle,
            10f);
        Assert(insideHit.Hit && insideHit.StartedInside, "circle ray starting inside must immediately hit");
        AssertNear(insideHit.Distance, 0f, "circle inside distance");

        Assert(
            !CollisionQueries2D.Raycast(
                new Ray2D(new Vec2D(0f, 2f), Vec2D.UnitX),
                circle,
                10f).Hit,
            "ray outside circle radius must miss");
        Assert(
            !CollisionQueries2D.Raycast(
                new Ray2D(Vec2D.Zero, -Vec2D.UnitX),
                circle,
                10f).Hit,
            "ray pointing away from circle must miss");
    }

    private static void SegmentRangeAndTinyNegativeCoordinateGeometryRemainFinite()
    {
        Aabb2D box = new Aabb2D(new Vec2D(-4f, -3f), new Vec2D(0.0001f, 0.0001f));
        Vec2D start = new Vec2D(-4.001f, -3f);
        Vec2D endBeforeBox = new Vec2D(-4.0002f, -3f);
        Vec2D endInsideBox = new Vec2D(-4f, -3f);

        Assert(
            Ray2D.TryCreateFromPoints(start, endBeforeBox, out Ray2D shortRay, out float shortDistance),
            "short segment should create a ray");
        Assert(
            !CollisionQueries2D.Raycast(shortRay, box, shortDistance).Hit,
            "segment ending before tiny box must miss");

        Assert(
            Ray2D.TryCreateFromPoints(start, endInsideBox, out Ray2D hitRay, out float hitDistance),
            "longer tiny segment should create a ray");
        RaycastResult2D hit = CollisionQueries2D.Raycast(hitRay, box, hitDistance);
        Assert(hit.Hit, "segment reaching tiny negative-coordinate box must hit");
        AssertNear(hit.Distance, 0.0009f, "tiny box entry distance", 0.00001f);
    }

    private static float DegreesToRadians(float degrees)
    {
        return degrees * (float)Math.PI / 180f;
    }

    internal static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    internal static void AssertNear(float actual, float expected, string description, float tolerance = 0.0001f)
    {
        Assert(Math.Abs(actual - expected) <= tolerance, $"{description}: expected {expected}, got {actual}");
    }

    internal static void AssertVec(Vec2D actual, Vec2D expected, string description, float tolerance = 0.0001f)
    {
        AssertNear(actual.X, expected.X, $"{description} X", tolerance);
        AssertNear(actual.Y, expected.Y, $"{description} Y", tolerance);
    }

    private static void ExpectThrows<TException>(Action action, string description)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"{description}: expected {typeof(TException).Name}");
    }
}
