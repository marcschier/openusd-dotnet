# Copyright (c) marcschier. Licensed under the MIT License.

import math
import random
import struct
import unittest


MAX_FLOAT = struct.unpack("<f", bytes.fromhex("ffff7f7f"))[0]
MIN_NORMAL = 2.0**-126
NO_LOG_VALUE = -MAX_FLOAT


def stored_float(value):
    return struct.unpack("<f", struct.pack("<f", value))[0]


def shader_float(value):
    if math.isnan(value):
        return math.nan
    if abs(value) > MAX_FLOAT:
        return math.copysign(math.inf, value)
    rounded = stored_float(value)
    return math.copysign(0.0, rounded) if abs(rounded) < MIN_NORMAL else rounded


LOG_TWO = stored_float(math.log(2))
LOG_MAX = stored_float(math.log(MAX_FLOAT))
LOG_HALF_MAX = stored_float(math.log(MAX_FLOAT / 2))


def log_positive(value):
    bits = struct.unpack("<I", struct.pack("<f", value))[0]
    if bits & 0x80000000 or not bits & 0x7FFFFFFF:
        return NO_LOG_VALUE
    if not bits & 0x7F800000:
        return shader_float(
            shader_float(math.log(bits & 0x007FFFFF))
            - shader_float(149.0 * LOG_TWO)
        )
    return shader_float(math.log(value))


def opacity(depth):
    if depth >= 0.125:
        return shader_float(1.0 - shader_float(math.exp(-depth)))
    result = depth
    term = depth
    for order in range(2, 9):
        term = shader_float(term * shader_float(depth / order))
        result = shader_float(result + term if order & 1 else result - term)
    return result


class LogSum:
    def __init__(self):
        self.maximum = NO_LOG_VALUE
        self.sum = 0.0
        self.compensation = 0.0

    def add(self, logarithm):
        if logarithm == NO_LOG_VALUE:
            return
        if self.sum == 0:
            self.maximum = logarithm
            self.sum = 1.0
            return
        contribution = 1.0
        if logarithm > self.maximum:
            scale = shader_float(math.exp(shader_float(self.maximum - logarithm)))
            self.sum = shader_float(self.sum * scale)
            self.compensation = shader_float(self.compensation * scale)
            self.maximum = logarithm
        else:
            contribution = shader_float(
                math.exp(shader_float(logarithm - self.maximum))
            )
        adjusted = shader_float(contribution - self.compensation)
        result = shader_float(self.sum + adjusted)
        self.compensation = shader_float(shader_float(result - self.sum) - adjusted)
        self.sum = result

    def value(self):
        if self.sum == 0:
            return NO_LOG_VALUE
        return shader_float(self.maximum + shader_float(math.log(self.sum)))


def source_log(fields):
    total = LogSum()
    depth = 0.0
    for field_depth, tint in fields:
        depth = shader_float(depth + field_depth)
        log_tint = log_positive(tint)
        if field_depth > 0 and log_tint != NO_LOG_VALUE:
            total.add(shader_float(log_tint + shader_float(math.log(field_depth))))
    return depth, total.value()


def color_from_log(logarithm):
    if logarithm == NO_LOG_VALUE:
        return 0.0
    if not math.isfinite(logarithm):
        return math.nan if math.isnan(logarithm) else (math.inf if logarithm > 0 else 0.0)
    if logarithm >= LOG_MAX:
        return MAX_FLOAT
    if logarithm >= LOG_HALF_MAX:
        half = shader_float(math.exp(shader_float(logarithm - LOG_TWO)))
        return shader_float(min(half, MAX_FLOAT / 2) * 2)
    return shader_float(math.exp(logarithm))


def clipped_field_log_depth(density, distance, field_opacity):
    return shader_float(
        shader_float(log_positive(density) + log_positive(distance))
        + log_positive(field_opacity)
    )


def step_log_depth(field_log_depth, density, scale, speed, step_length, integral):
    result = shader_float(field_log_depth + log_positive(density))
    result = shader_float(result - shader_float(math.log(scale)))
    result = shader_float(result + log_positive(speed))
    result = shader_float(result + log_positive(step_length))
    return shader_float(result - shader_float(math.log(integral)))


def attenuate_background(value, depth):
    third = shader_float(math.exp(shader_float(-depth / 3.0)))
    first = shader_float(value * third)
    second = shader_float(first * third)
    return shader_float(second * third)


def composite(segments, exposure=0.0):
    flat_fields = [field for segment in segments for field in segment]
    all_depth, weighted_tint = source_log(flat_fields)
    if 0 < all_depth <= stored_float(0.001):
        if weighted_tint == NO_LOG_VALUE:
            return 0.0
        exposed_log = shader_float(
            shader_float(weighted_tint - shader_float(math.log(all_depth)))
            + shader_float(exposure * LOG_TWO)
        )
        return shader_float(color_from_log(exposed_log) * opacity(all_depth))

    total = LogSum()
    optical_depth = 0.0
    for fields in segments:
        depth, source = source_log(fields)
        if depth <= 0:
            continue
        alpha = opacity(depth)
        if source != NO_LOG_VALUE:
            term = shader_float(
                shader_float(
                    shader_float(source - shader_float(math.log(depth)))
                    + shader_float(math.log(alpha))
                )
                - optical_depth
            )
            total.add(term)
        optical_depth = shader_float(optical_depth + depth)
    if optical_depth <= 0:
        return 0.0
    logarithm = total.value()
    if logarithm == NO_LOG_VALUE:
        return 0.0
    alpha = opacity(optical_depth)
    exposed_log = shader_float(
        shader_float(logarithm - shader_float(math.log(alpha)))
        + shader_float(exposure * LOG_TWO)
    )
    return shader_float(color_from_log(exposed_log) * alpha)


def reference(segments, exposure=0.0):
    result = 0.0
    transmittance = 1.0
    for fields in segments:
        depth = sum(field_depth for field_depth, _ in fields)
        if depth:
            source = sum(field_depth * tint for field_depth, tint in fields) / depth
            result += transmittance * -math.expm1(-depth) * source
            transmittance *= math.exp(-depth)
    return result * 2.0**exposure


class VolumeLogMathTests(unittest.TestCase):
    def assert_matches_reference(self, segments, exposure=0.0, tolerance=0.001):
        expected = reference(segments, exposure)
        actual = composite(segments, exposure)
        self.assertGreaterEqual(expected, MIN_NORMAL)
        self.assertTrue(math.isfinite(actual))
        self.assertLess(abs(actual / expected - 1), tolerance)

    def test_extreme_weighted_and_attenuated_contributions(self):
        cases = [
            [[(stored_float(0.3663415014743805), MAX_FLOAT)]],
            [[(stored_float(0.0001), MAX_FLOAT)]],
            [[(stored_float(0.3663415014743805), MAX_FLOAT),
              (stored_float(31.201824), MAX_FLOAT)]],
            [[(stored_float(1e-30), MAX_FLOAT), (stored_float(1e30), 0.0)]],
            [[(100.0, 0.0)], [(1.0, MAX_FLOAT)]],
            [[(stored_float(1e-8), stored_float(1e-30))], [(100.0, 0.0)], [(1.0, 1e6)]],
            [[(stored_float(430342976), stored_float(3.1047865e-24)),
              (stored_float(2.4619956e23), stored_float(1.13225936e-37))]],
        ]
        for segments in cases:
            with self.subTest(segments=segments):
                self.assert_matches_reference(segments)

    def test_subnormal_input_tint_is_retained_until_exposure(self):
        self.assert_matches_reference([[(1.0, stored_float(1e-40))]], exposure=64)

    def test_zero_and_negative_tints_do_not_emit(self):
        self.assertEqual(composite([[(1.0, 0.0), (1.0, -1.0)]]), 0.0)

    def test_nonfinite_logarithms_are_not_saturated(self):
        self.assertTrue(math.isinf(color_from_log(math.inf)))
        self.assertTrue(math.isnan(color_from_log(math.nan)))
        self.assertEqual(color_from_log(-math.inf), 0.0)

    def test_nonfinite_contribution_is_not_mistaken_for_an_empty_sum(self):
        total = LogSum()
        total.add(0.0)
        total.add(math.nan)
        self.assertTrue(math.isnan(total.value()))

    def test_compensated_sum_across_maximum_sampling_lattice(self):
        segments = [[(stored_float(0.0001), 1.0)]] * 7117
        self.assert_matches_reference(segments)

    def test_clipped_depth_can_flush_before_it_is_logged(self):
        density = stored_float(1e-37)
        local_distance = stored_float(0.05)
        self.assertEqual(shader_float(density * local_distance), 0.0)
        self.assertGreater(MAX_FLOAT * density * local_distance, 1.0)

    def test_clipped_subnormal_depth_remains_visible_in_log_composite(self):
        density = stored_float(1e-37)
        distance = stored_float(0.05)
        for field_opacity in [stored_float(1.0), stored_float(0.1)]:
            with self.subTest(field_opacity=field_opacity):
                log_depth = clipped_field_log_depth(density, distance, field_opacity)
                actual = color_from_log(shader_float(log_positive(MAX_FLOAT) + log_depth))
                expected = MAX_FLOAT * density * distance * field_opacity
                self.assertLess(abs(actual / expected - 1), 0.0001)

    def test_clipped_subnormal_depth_contributes_with_a_dominant_dark_field(self):
        density = stored_float(1e-37)
        distance = stored_float(0.05)
        field_log = clipped_field_log_depth(density, distance, 1.0)
        self.assertEqual(shader_float(density * distance), 0.0)
        log_step = step_log_depth(field_log, 1.0, 1.0, 1.0, distance, distance)
        log_source = shader_float(log_positive(MAX_FLOAT) + log_step)
        dark_depth = distance
        log_alpha = shader_float(math.log(opacity(dark_depth)))
        output_log = shader_float(
            shader_float(log_source - shader_float(math.log(dark_depth))) + log_alpha
        )
        actual = color_from_log(output_log)
        expected = MAX_FLOAT * density * -math.expm1(-distance)
        self.assertLess(abs(actual / expected - 1), 0.0001)

    def test_straight_alpha_cannot_represent_deep_background_transmission(self):
        alpha = opacity(100.0)
        self.assertEqual(alpha, 1.0)
        self.assertEqual(shader_float(MAX_FLOAT * (1.0 - alpha)), 0.0)
        self.assertGreater(MAX_FLOAT * math.exp(-100.0), 1e-5)

    def test_background_attenuation_preserves_normal_signed_results(self):
        for depth in [0.0, 0.1, 10.0, 20.0, 100.0, 150.0, 175.0]:
            for value in [MAX_FLOAT, -MAX_FLOAT]:
                with self.subTest(depth=depth, value=value):
                    actual = attenuate_background(value, depth)
                    expected = value * math.exp(-depth)
                    self.assertGreaterEqual(abs(expected), MIN_NORMAL)
                    self.assertLess(abs(actual / expected - 1), 0.0001)

    def test_positive_finite_ranges_against_double_composite(self):
        generator = random.Random(3503)
        for _ in range(2000):
            segments = []
            for _ in range(generator.randint(1, 8)):
                fields = []
                for _ in range(generator.randint(1, 8)):
                    depth = stored_float(10 ** generator.uniform(-37.9, 1.8))
                    tint = stored_float(10 ** generator.uniform(-37.9, 38.5))
                    fields.append((depth, tint if generator.random() > 0.15 else 0.0))
                segments.append(fields)
            expected = reference(segments)
            if expected >= MIN_NORMAL:
                self.assert_matches_reference(segments)

    def test_exposure_ranges_against_double_composite(self):
        generator = random.Random(3504)
        for _ in range(1000):
            tint = stored_float(10 ** generator.uniform(-40, 38))
            depth = stored_float(10 ** generator.uniform(-30, 2))
            maximum_exposure = min(127.0, math.log2(MAX_FLOAT / tint))
            exposure = stored_float(generator.uniform(-120.0, maximum_exposure))
            segments = [[(depth, tint)]]
            expected = reference(segments, exposure)
            if MIN_NORMAL <= expected <= MAX_FLOAT:
                self.assert_matches_reference(segments, exposure)


if __name__ == "__main__":
    unittest.main()
