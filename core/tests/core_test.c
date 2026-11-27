#include "../keycat_core.h"
#include <math.h>
#include <stdio.h>
#include <stdlib.h>

#define CHECK(cond) do { \
    if (!(cond)) { \
        fprintf(stderr, "FAIL %s:%d: %s\n", __FILE__, __LINE__, #cond); \
        return 1; \
    } \
} while (0)

static int near_eq(double a, double b, double tol) {
    return fabs(a - b) <= tol;
}

int main(void) {
    KCSceneState s;
    KCOfficeArmGeometry arm;
    KCCapLayout cap;
    KCHeadPose head;

    CHECK(KC_DEFAULT_CAT == 0);
    CHECK(KC_DEFAULT_KEYBOARD == 4);
    CHECK(KC_DEFAULT_MOUSE == 4);

    kc_scene_init(&s, 10.0);
    CHECK(s.catStyle == 0);
    CHECK(s.keyboardStyle == 4);
    CHECK(s.mouseStyle == 4);

    /* Switching from typing cat to office colleague clears residual key/move state */
    kc_scene_strike(&s, 65, kc_pt(250.0, 250.0), 10.5);
    kc_scene_advance(&s, 10.6, 0.5);
    kc_scene_set_character(&s, 4);
    CHECK(kc_is_person(s.catStyle) == 1);
    CHECK(kc_scene_office_pose(&s) == KC_POSE_IDLE);

    /* Four rapid keys trigger two-handed office typing */
    kc_scene_strike(&s, 65, kc_pt(250.0, 250.0), 11.00);
    kc_scene_strike(&s, 66, kc_pt(250.0, 250.0), 11.06);
    kc_scene_strike(&s, 67, kc_pt(250.0, 250.0), 11.12);
    kc_scene_strike(&s, 68, kc_pt(250.0, 250.0), 11.18);
    kc_scene_advance(&s, 11.20, 0.5);
    CHECK(kc_scene_office_pose(&s) == KC_POSE_WORKING);
    CHECK(s.officeLeft == 1 && s.officeRight == 1);

    /* Head hover overrides sleeping even after 3+ seconds */
    kc_scene_pointer(&s, kc_pt(214.0, 105.0), 0, 0, 11.30, kc_pt(0.0, 0.0), 1);
    kc_scene_advance(&s, 15.0, 0.5);
    CHECK(kc_scene_office_pose(&s) == KC_POSE_HOVER);

    /* Leaving head and resting for 3 seconds triggers sleep */
    kc_scene_pointer(&s, kc_pt(305.0, 260.0), 0, 0, 15.1, kc_pt(0.0, 0.0), 0);
    kc_scene_advance(&s, 18.2, 0.5);
    CHECK(kc_scene_office_pose(&s) == KC_POSE_SLEEPING);

    /* Head click lasts 1.5s */
    kc_scene_click_office_head(&s, 18.3);
    kc_scene_advance(&s, 19.0, 0.5);
    CHECK(kc_scene_office_pose(&s) == KC_POSE_CLICK);
    kc_scene_advance(&s, 19.9, 0.5);
    CHECK(kc_scene_office_pose(&s) == KC_POSE_SLEEPING || kc_scene_office_pose(&s) == KC_POSE_IDLE);

    /* Office arm solver outputs symmetric finite geometry */
    arm = kc_solve_office_arm(kc_pt(188.0, 171.0), kc_pt(214.0, 250.0), -1, 0);
    CHECK(near_eq(arm.handScaleX, 1.16, 1e-6));
    CHECK(near_eq(arm.handScaleY, 1.16, 1e-6));
    CHECK(arm.handAngleDeg <= 31.0 && arm.handAngleDeg >= -31.0);

    /* Cap layout & cat head pose */
    cap = kc_cap_layout(0.5, 1, kc_pt(250.0, 240.0), 4.0);
    CHECK(cap.expired == 0 && cap.opacity > 0.9);
    cap = kc_cap_layout(5.9, 1, kc_pt(250.0, 240.0), 4.0);
    CHECK(cap.expired == 1);

    kc_scene_set_character(&s, 0);
    head = kc_cat_head_pose(&s);
    CHECK(head.position.x > 150.0 && head.position.y > 70.0);

    CHECK(kc_compare_version_triples(1, 10, 2, 1, 11, 0) < 0);
    CHECK(kc_compare_version_triples(1, 11, 0, 1, 11, 0) == 0);
    CHECK(kc_compare_version_triples(2, 0, 0, 1, 99, 9) > 0);

    for (int i = 4; i <= 13; i++) {
        CHECK(kc_is_person(i) == 1);
    }
    CHECK(kc_is_person(3) == 0);
    for (int i = 14; i <= 17; i++) {
        CHECK(kc_is_person(i) == 0);
        kc_scene_set_character(&s, i);
        kc_scene_reset_input(&s, 20.0);
        kc_scene_advance(&s, 20.2, 0.5);
        KCHeadPose hp = kc_cat_head_pose(&s);
        CHECK(hp.position.x > 100.0 && hp.position.y > 95.0 && hp.position.y < 115.0);
        CHECK(near_eq(s.left.y, 233.0, 1e-6) && near_eq(s.right.y, 233.0, 1e-6));
        kc_scene_strike(&s, 65, kc_pt(190.0, 250.0), 20.25);
        for (int step = 1; step <= 12; step++) {
            kc_scene_advance(&s, 20.25 + step * 0.03, 0.5);
        }
        CHECK(s.right.y > 205.0 && s.right.y < 230.0);
    }

    /* Switching directly from OfficeColleague to a pet resets paws to pet rest immediately */
    kc_scene_set_character(&s, 12);
    kc_scene_reset_office(&s);
    CHECK(near_eq(s.left.x, s.jKey.x, 1e-6) && near_eq(s.right.y, s.spaceKey.y, 1e-6));
    kc_scene_set_character(&s, 15);
    CHECK(near_eq(s.left.x, 192.0, 1e-6) && near_eq(s.left.y, 233.0, 1e-6));
    CHECK(near_eq(s.right.x, 229.0, 1e-6) && near_eq(s.right.y, 233.0, 1e-6));

    /* Office body pose leans toward typed keys and resets cleanly when idle */
    kc_scene_set_character(&s, 12);
    kc_scene_reset_office(&s);
    kc_scene_strike(&s, 65, kc_pt(145.0, 240.0), 25.00);
    kc_scene_advance(&s, 25.05, 0.5);
    {
        KCOfficeBodyPose leftLean = kc_office_body_pose(&s, 214.0, 242.0, kc_pt(190.0, 177.0), kc_pt(238.0, 177.0));
        CHECK(leftLean.offsetX < -0.5 && leftLean.tiltDeg < -0.2 && leftLean.offsetY > 0.2);
        CHECK(leftLean.leftShoulder.x < 190.0);
    }
    kc_scene_strike(&s, 76, kc_pt(290.0, 240.0), 25.10);
    kc_scene_advance(&s, 25.25, 0.5);
    {
        KCOfficeBodyPose rightLean = kc_office_body_pose(&s, 214.0, 242.0, kc_pt(190.0, 177.0), kc_pt(238.0, 177.0));
        CHECK(rightLean.offsetX > 0.2 && rightLean.tiltDeg > 0.1);
    }
    kc_scene_advance(&s, 26.20, 0.5);
    {
        KCOfficeBodyPose restPose = kc_office_body_pose(&s, 214.0, 242.0, kc_pt(190.0, 177.0), kc_pt(238.0, 177.0));
        CHECK(kc_scene_office_pose(&s) == KC_POSE_IDLE);
        CHECK(near_eq(restPose.offsetX, 0.0, 1e-9) && near_eq(restPose.offsetY, 0.0, 1e-9) && near_eq(restPose.tiltDeg, 0.0, 1e-9));
    }

    /* Slow typing does NOT trigger forceful motion or VFX; rapid typing DOES */
    kc_scene_set_character(&s, 0);
    kc_scene_reset_input(&s, 28.0);
    kc_scene_strike(&s, 65, kc_pt(285.0, 245.0), 28.00);
    kc_scene_advance(&s, 28.02, 0.5);
    CHECK(near_eq(kc_cat_impact_intensity(&s), 0.0, 1e-9));
    kc_scene_strike(&s, 83, kc_pt(275.0, 250.0), 28.35);
    kc_scene_strike(&s, 68, kc_pt(265.0, 250.0), 28.70);
    kc_scene_strike(&s, 70, kc_pt(255.0, 250.0), 29.05);
    kc_scene_advance(&s, 29.08, 0.5);
    {
        KCCatMotion slow = kc_cat_motion(&s);
        CHECK(near_eq(slow.fury, 0.0, 1e-9) && near_eq(slow.slam, 0.0, 1e-9));
        CHECK(near_eq(slow.leftWhip, 0.0, 1e-9) && near_eq(slow.rightWhip, 0.0, 1e-9));
        CHECK(near_eq(slow.offsetY, 0.0, 1e-9) && near_eq(slow.deskOffsetY, 0.0, 1e-9));
        CHECK(near_eq(slow.scaleX, 1.0, 1e-9) && near_eq(slow.scaleY, 1.0, 1e-9));
        CHECK(near_eq(kc_cat_impact_intensity(&s), 0.0, 1e-9));
    }

    kc_scene_reset_input(&s, 30.0);
    kc_scene_strike(&s, 65, kc_pt(285.0, 245.0), 30.00);
    kc_scene_strike(&s, 83, kc_pt(275.0, 250.0), 30.04);
    CHECK(near_eq(kc_cat_impact_intensity(&s), 0.0, 1e-9)); /* 2 isolated keys stay calm */
    kc_scene_strike(&s, 68, kc_pt(265.0, 250.0), 30.08);
    CHECK(kc_cat_impact_intensity(&s) >= 0.32);
    kc_scene_advance(&s, 30.10, 0.5);
    {
        KCCatMotion motion = kc_cat_motion(&s);
        KCImpactLayout ring0 = kc_impact_layout(0.02, motion.fury, 12.0);
        KCImpactLayout ring1 = kc_impact_layout(0.14, motion.fury, 12.0);
        KCImpactLayout ringOld = kc_impact_layout(0.32, motion.fury, 12.0);
        CHECK(motion.fury > 0.5 && motion.slam > 0.5);
        CHECK(motion.leftWhip > 0.14 || motion.rightWhip > 0.14);
        CHECK(motion.scaleX > 1.0 && motion.scaleY < 1.0);
        CHECK(motion.offsetY > 1.0 && motion.deskOffsetY > 0.5);
        CHECK(ring0.expired == 0 && ring1.expired == 0 && ring1.ringRadius > ring0.ringRadius);
        CHECK(ringOld.expired == 1);
    }
    /* Slowing down right after a fast burst stops spawning impacts */
    kc_scene_strike(&s, 70, kc_pt(250.0, 250.0), 30.48);
    CHECK(near_eq(kc_cat_impact_intensity(&s), 0.0, 1e-9));
    for (int step = 1; step <= 40; step++) {
        kc_scene_advance(&s, 30.48 + step * 0.05, 0.5);
    }
    {
        KCCatMotion calm = kc_cat_motion(&s);
        CHECK(near_eq(calm.slam, 0.0, 1e-6) && near_eq(calm.fury, 0.0, 1e-6));
        CHECK(near_eq(calm.offsetY, 0.0, 1e-6) && near_eq(calm.deskOffsetY, 0.0, 1e-6));
        CHECK(near_eq(calm.scaleX, 1.0, 1e-6) && near_eq(calm.scaleY, 1.0, 1e-6));
    }

    /* Quiet fxLevel dials back body recoil, zeros desk recoil, and suppresses impacts while keeping stats */
    kc_scene_init(&s, 40.0);
    s.fxLevel = KC_FX_QUIET;
    kc_scene_strike(&s, 65, kc_pt(285.0, 245.0), 40.00);
    kc_scene_strike(&s, 83, kc_pt(275.0, 250.0), 40.04);
    kc_scene_strike(&s, 68, kc_pt(265.0, 250.0), 40.08);
    kc_scene_strike(&s, 70, kc_pt(255.0, 250.0), 40.12);
    kc_scene_advance(&s, 40.14, 0.5);
    {
        KCCatMotion quiet = kc_cat_motion(&s);
        CHECK(s.todayKeyCount == 4 && s.todayBurstCount >= 1);
        CHECK(quiet.fury <= 0.270001);
        CHECK(near_eq(quiet.deskOffsetX, 0.0, 1e-9) && near_eq(quiet.deskOffsetY, 0.0, 1e-9) && near_eq(quiet.deskTiltDeg, 0.0, 1e-9));
        CHECK(near_eq(kc_cat_impact_intensity(&s), 0.0, 1e-9));
    }

    /* Break reminder fades smoothly when active work hits 2700s and dismisses cleanly */
    s.activeWorkSeconds = 2699.95;
    kc_scene_strike(&s, 74, kc_pt(250.0, 250.0), 41.00);
    kc_scene_advance(&s, 41.10, 0.5);
    CHECK(s.breakBubbleUntil > 41.10);
    CHECK(kc_scene_break_opacity(&s, 41.80) > 0.9);
    kc_scene_dismiss_break(&s);
    CHECK(near_eq(kc_scene_break_opacity(&s, 41.85), 0.0, 1e-9));

    printf("PASS core_test\n");
    return 0;
}
