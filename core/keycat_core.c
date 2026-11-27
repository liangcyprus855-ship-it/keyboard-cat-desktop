#include "keycat_core.h"
#include <math.h>

static double kc_hypot2(double dx, double dy) {
    return sqrt(dx * dx + dy * dy);
}

static KCPoint kc_add(KCPoint a, KCPoint b) {
    KCPoint r;
    r.x = a.x + b.x;
    r.y = a.y + b.y;
    return r;
}

static KCPoint kc_sub(KCPoint a, KCPoint b) {
    KCPoint r;
    r.x = a.x - b.x;
    r.y = a.y - b.y;
    return r;
}

static KCPoint kc_shift(KCPoint p, KCPoint dir, double scale) {
    KCPoint r;
    r.x = p.x + dir.x * scale;
    r.y = p.y + dir.y * scale;
    return r;
}

static KCPoint kc_unit(KCPoint v) {
    double length = kc_hypot2(v.x, v.y);
    if (length > 0.001) {
        return kc_pt(v.x / length, v.y / length);
    }
    return kc_pt(0.0, 1.0);
}

static KCPoint kc_normal(KCPoint v) {
    return kc_pt(-v.y, v.x);
}

static double kc_click_glow(double time, int down, double onset) {
    return down != 0 ? 1.0 : 1.0 - kc_smooth((time - onset - 0.10) / 0.14);
}

static double kc_cat_burst_score(double tPrev, double tPrev2, double now) {
    double dtKey = fmax(0.0, now - tPrev);
    double span3 = fmax(0.001, now - tPrev2);
    double effInterval = fmax(dtKey, span3 * 0.5);
    if (effInterval >= 0.205) {
        return 0.0;
    }
    return kc_clamp((0.205 - effInterval) / 0.125, 0.0, 1.0);
}

KCPoint kc_pt(double x, double y) {
    KCPoint p;
    p.x = x;
    p.y = y;
    return p;
}

int kc_is_person(int catStyle) {
    return catStyle >= 4 && catStyle <= 13 ? 1 : 0;
}

double kc_clamp(double value, double low, double high) {
    return fmax(low, fmin(high, value));
}

double kc_smooth(double x) {
    x = kc_clamp(x, 0.0, 1.0);
    return x * x * (3.0 - 2.0 * x);
}

double kc_beat(double age) {
    if (age < 0.0 || age > 0.24) {
        return 0.0;
    }
    return age < 0.065 ? 1.0 : 1.0 - kc_smooth((age - 0.065) / 0.175);
}

double kc_pulse(double age, double duration) {
    if (age < 0.0 || age > duration) {
        return 0.0;
    }
    return fmin(1.0, sin(3.14159265358979323846 * age / duration) * 1.4);
}

KCPoint kc_mix_point(KCPoint a, KCPoint b, double t) {
    return kc_pt(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t);
}

double kc_cat_lean(const KCSceneState *s) {
    return kc_smooth((157.0 - s->left.x) / 50.0);
}

KCHeadPose kc_cat_head_pose(const KCSceneState *s) {
    KCHeadPose pose;
    double lean = kc_cat_lean(s);
    if (s->catStyle < 2) {
        pose.position = kc_pt(214.0 - 45.0 * lean + 3.2 * s->gazeX,
                              86.0 + sin(s->time * 1.7) * 0.72 + s->gazeY * 2.0 + lean * 1.2);
        pose.tiltDeg = 2.8 * s->gazeX - 3.2 * lean;
        return pose;
    }
    if (s->catStyle >= 14 && s->catStyle <= 17) {
        double topY = s->catStyle == 14 ? 98.92
                    : (s->catStyle == 15 ? 108.21
                    : (s->catStyle == 16 ? 100.98 : 105.11));
        double rightReach = kc_clamp((s->right.x - 248.0) / 55.0, 0.0, 0.75);
        double netLean = lean * 0.85 - rightReach * 0.35;
        double shiftX = -11.0 * lean + 5.5 * rightReach;
        double bodyDeg = -2.2 * lean + 1.5 * rightReach;
        double rad = bodyDeg * 3.14159265358979323846 / 180.0;
        double c = cos(rad), sn = sin(rad);
        double hx0 = 0.24 * netLean * (topY - 246.0);
        double hy0 = topY - 247.0;
        pose.position = kc_pt(214.0 + hx0 * c - hy0 * sn + shiftX + 2.5 * s->gazeX,
                              247.0 + hx0 * sn + hy0 * c + 2.0 * s->gazeY + sin(s->time * 1.7) * 0.6);
        pose.tiltDeg = bodyDeg + 2.8 * s->gazeX - 1.6 * lean + 1.0 * rightReach;
        return pose;
    }
    double top = s->catStyle == 2 ? 118.0 : (237.0 - 119.0 * 1.025);
    double baseTilt = s->catStyle == 2 ? 3.2 : -2.25;
    pose.position = kc_pt(214.0 - 40.0 * lean + 2.5 * s->gazeX,
                          top - 2.0 + 2.0 * s->gazeY + sin(s->time * 1.7) * 0.6);
    pose.tiltDeg = baseTilt + 2.8 * s->gazeX - 3.0 * lean;
    return pose;
}

void kc_scene_init(KCSceneState *s, double now) {
    s->catStyle = KC_DEFAULT_CAT;
    s->keyboardStyle = KC_DEFAULT_KEYBOARD;
    s->mouseStyle = KC_DEFAULT_MOUSE;
    s->labelsEnabled = 1;
    s->privacyMode = 0;
    s->fxLevel = KC_FX_VIBRANT;
    s->deskProp = KC_PROP_NONE;
    s->breakReminderEnabled = 1;
    s->todayKeyCount = 0;
    s->todayBurstCount = 0;
    s->lastBurstActive = 0;
    s->activeWorkSeconds = 0.0;
    s->breakBubbleUntil = -10.0;
    s->previousCharacter = s->catStyle;
    s->mousePaw = kc_pt(104.0, 264.0);
    s->spaceKey = kc_pt(214.0, 260.0);
    s->jKey = kc_pt(214.0, 250.0);
    s->fKey = kc_pt(245.0, 250.0);
    kc_scene_reset_input(s, now);
}

void kc_scene_reset_office(KCSceneState *s) {
    int i = 0;
    s->officeClickUntil = -1.0;
    s->lastOfficeInput = -10.0;
    s->officeIdleSince = s->time;
    s->officeTwoHandsUntil = s->time;
    s->lastOfficeStrikeWasLeft = 0;
    s->officeLeft = 0;
    s->officeRight = 0;
    s->officeLeanX = 0.0;
    s->officeLeanY = 0.0;
    s->officeTiltDeg = 0.0;
    s->catLastStrikeWasLeft = 0;
    s->catNextKey = 0;
    s->catNextClick = 0;
    s->catSlam = 0.0;
    s->catFury = 0.0;
    s->catFastAt = s->time - 10.0;
    s->catStrikeSpeed = 0.0;
    s->catLeanX = 0.0;
    s->catBounceY = 0.0;
    s->catTiltDeg = 0.0;
    s->deskOffsetX = 0.0;
    s->deskOffsetY = 0.0;
    s->deskTiltDeg = 0.0;
    for (i = 0; i < 4; i++) {
        s->officeRecentKeys[i] = s->time - 10.0;
        s->catRecentKeys[i] = s->time - 10.0;
        s->catRecentClicks[i] = s->time - 10.0;
    }
    s->officeNextKey = 0;
    s->headHovered = 0;
    s->pointerKnown = 0;
    s->lastKey = s->time - 10.0;
    s->lastMove = s->time - 10.0;
    s->leftAt = s->time - 10.0;
    s->rightAt = s->time - 10.0;
    s->grip = 0.0;
    if (kc_is_person(s->catStyle) != 0) {
        s->left = s->jKey;
        s->freeLeft = s->jKey;
        s->right = s->spaceKey;
    } else {
        s->left = kc_pt(192.0, 233.0);
        s->freeLeft = kc_pt(192.0, 233.0);
        s->right = kc_pt(229.0, 233.0);
    }
}

void kc_scene_reset_input(KCSceneState *s, double now) {
    s->time = now;
    s->left = kc_pt(192.0, 233.0);
    s->freeLeft = kc_pt(192.0, 233.0);
    s->right = kc_pt(229.0, 233.0);
    s->leftKey = kc_pt(0.0, 0.0);
    s->rightKey = kc_pt(0.0, 0.0);
    s->pointer = kc_pt(0.0, 0.0);
    s->lastPointer = kc_pt(0.0, 0.0);
    s->mouseOffset = kc_pt(0.0, 0.0);
    s->localTravel = kc_pt(0.0, 0.0);
    s->monitorPosition = kc_pt(0.0, 0.0);
    s->pointerKnown = 0;
    s->leftDown = 0;
    s->rightDown = 0;
    s->headHovered = 0;
    s->lastMove = now - 10.0;
    s->lastKey = now - 10.0;
    s->leftAt = now - 10.0;
    s->rightAt = now - 10.0;
    s->leftClickAt = now - 10.0;
    s->rightClickAt = now - 10.0;
    s->lastPet = now - 10.0;
    s->nextBlink = now + 2.7;
    s->grip = 0.0;
    s->gazeX = 0.0;
    s->gazeY = 0.0;
    s->leftActivity = 0.0;
    s->rightActivity = 0.0;
    s->blink = 0.0;
    s->leftClickGlow = 0.0;
    s->rightClickGlow = 0.0;
    kc_scene_reset_office(s);
}

void kc_scene_set_character(KCSceneState *s, int nextCatStyle) {
    int wasPerson = kc_is_person(s->catStyle);
    int nextPerson = kc_is_person(nextCatStyle);
    if (s->catStyle == nextCatStyle) {
        return;
    }
    s->catStyle = nextCatStyle;
    if (wasPerson != 0 || nextPerson != 0) {
        kc_scene_reset_office(s);
    }
}

void kc_scene_click_office_head(KCSceneState *s, double now) {
    if (kc_is_person(s->catStyle) == 0) {
        return;
    }
    s->officeClickUntil = now + 1.5;
    if (s->breakBubbleUntil > now) {
        s->breakBubbleUntil = -10.0;
    }
}

int kc_scene_office_pose(const KCSceneState *s) {
    double activity = fmax(s->lastKey, s->lastMove);
    double idleAnchor = fmax(s->officeIdleSince, fmax(s->lastOfficeInput, activity));
    if (s->time < s->officeClickUntil) {
        return KC_POSE_CLICK;
    }
    if (s->headHovered != 0) {
        return KC_POSE_HOVER;
    }
    if (s->leftDown == 0 && s->rightDown == 0 && s->time - idleAnchor >= 3.0) {
        return KC_POSE_SLEEPING;
    }
    if (s->lastOfficeInput >= 0.0 && (s->leftDown != 0 || s->rightDown != 0 || s->time - activity < 0.8)) {
        return KC_POSE_WORKING;
    }
    return KC_POSE_IDLE;
}

int kc_scene_using_mouse(const KCSceneState *s, double now) {
    double timeout = kc_is_person(s->catStyle) != 0 ? 0.24 : 0.83;
    return (s->leftDown != 0 || s->rightDown != 0 || now - s->lastMove < timeout) ? 1 : 0;
}

void kc_scene_mark_mouse(KCSceneState *s, double now) {
    if (kc_is_person(s->catStyle) != 0) {
        s->lastOfficeInput = now;
        s->officeIdleSince = now;
    }
    if (kc_scene_using_mouse(s, now) == 0 && s->leftAt > s->rightAt && now - s->leftAt < 0.24) {
        s->rightKey = s->leftKey;
        s->rightAt = s->leftAt;
        s->leftAt = now - 10.0;
    }
    s->lastMove = now;
}

void kc_scene_mouse_button(KCSceneState *s, int button1or2, int down, double now) {
    int flag = down != 0 ? 1 : 0;
    if (button1or2 == 1) {
        if (s->leftDown == flag) {
            return;
        }
        kc_scene_mark_mouse(s, now);
        s->leftDown = flag;
        if (flag != 0) {
            s->leftClickAt = now;
        }
    } else if (button1or2 == 2) {
        if (s->rightDown == flag) {
            return;
        }
        kc_scene_mark_mouse(s, now);
        s->rightDown = flag;
        if (flag != 0) {
            s->rightClickAt = now;
        }
    }
    if (flag != 0 && s->headHovered != 0) {
        s->lastPet = now;
        if (s->breakBubbleUntil > now) {
            s->breakBubbleUntil = -10.0;
        }
    }
    if (kc_is_person(s->catStyle) == 0 && flag != 0) {
        int prev = (s->catNextClick + 3) % 4;
        int prev2 = (s->catNextClick + 2) % 4;
        double clickBurst = kc_cat_burst_score(s->catRecentClicks[prev], s->catRecentClicks[prev2], now);
        s->catRecentClicks[s->catNextClick] = now;
        s->catNextClick = (s->catNextClick + 1) % 4;
        if (clickBurst > 0.0) {
            s->catFastAt = now;
            s->catSlam = fmax(s->catSlam, 0.35 + 0.30 * clickBurst);
            s->catFury = kc_clamp(s->catFury + 0.12 + 0.12 * clickBurst, 0.22, 0.70);
        }
    }
}

void kc_scene_mouse_scroll(KCSceneState *s, double now) {
    if (kc_is_person(s->catStyle) != 0) {
        kc_scene_mark_mouse(s, now);
    }
}

void kc_scene_pointer(KCSceneState *s, KCPoint p, int leftDown, int rightDown, double now,
                      KCPoint monitorPosition, int headHit) {
    double dx = p.x - s->lastPointer.x;
    double dy = p.y - s->lastPointer.y;
    if (s->pointerKnown != 0 && (dx * dx + dy * dy) > 0.000001) {
        s->localTravel.x = kc_clamp(s->localTravel.x + dx * 0.13, -5.5, 5.5);
        s->localTravel.y = kc_clamp(s->localTravel.y + dy * 0.13, -6.0, 6.0);
        kc_scene_mark_mouse(s, now);
    }
    s->monitorPosition = monitorPosition;
    if (headHit != 0 && s->headHovered == 0) {
        s->lastPet = now;
    }
    s->headHovered = headHit != 0 ? 1 : 0;
    s->pointerKnown = 1;
    s->pointer = p;
    s->lastPointer = p;
    kc_scene_mouse_button(s, 1, leftDown, now);
    kc_scene_mouse_button(s, 2, rightDown, now);
}

int kc_scene_strike(KCSceneState *s, int vk, KCPoint target, double now) {
    int office = kc_is_person(s->catStyle);
    int useLeft = (target.x < 218.0 || (vk == 32 && s->rightAt > s->leftAt)) ? 1 : 0;
    int strikeWithLeft = 0;
    s->lastKey = now;
    s->todayKeyCount++;
    if (office != 0) {
        s->lastOfficeInput = now;
        s->officeRecentKeys[s->officeNextKey] = now;
        s->officeNextKey = (s->officeNextKey + 1) % 4;
        if (now - s->officeRecentKeys[s->officeNextKey] <= 0.43) {
            s->officeTwoHandsUntil = now + 0.65;
        }
        if (now < s->officeTwoHandsUntil) {
            if (target.x < 214.0) {
                useLeft = 1;
            } else if (target.x > 226.0) {
                useLeft = 0;
            } else {
                useLeft = s->lastOfficeStrikeWasLeft == 0 ? 1 : 0;
            }
        } else {
            useLeft = target.x < 214.0 ? 1 : 0;
        }
    }
    strikeWithLeft = (kc_scene_using_mouse(s, now) == 0 && useLeft != 0) ? 1 : 0;
    if (office != 0) {
        s->lastOfficeStrikeWasLeft = strikeWithLeft;
    } else {
        int prev = (s->catNextKey + 3) % 4;
        int prev2 = (s->catNextKey + 2) % 4;
        double burst = kc_cat_burst_score(s->catRecentKeys[prev], s->catRecentKeys[prev2], now);
        s->catRecentKeys[s->catNextKey] = now;
        s->catNextKey = (s->catNextKey + 1) % 4;
        s->catLastStrikeWasLeft = strikeWithLeft;
        s->catStrikeSpeed = burst;
        if (burst > 0.0) {
            s->catFastAt = now;
            s->catSlam = fmax(s->catSlam, 0.50 + 0.50 * burst);
            s->catFury = kc_clamp(s->catFury + 0.24 + 0.31 * burst, 0.30 + 0.26 * burst, 1.0);
        }
    }
    if (strikeWithLeft != 0) {
        s->leftAt = now;
        s->leftKey = target;
    } else {
        s->rightAt = now;
        s->rightKey = target;
    }
    return strikeWithLeft;
}

void kc_scene_advance(KCSceneState *s, double now, double blinkRandom01) {
    double dt = kc_clamp(now - s->time, 0.001, 0.1);
    double typing = 0.0;
    double hold = 0.0;
    int office = kc_is_person(s->catStyle);
    int usingMouse = kc_scene_using_mouse(s, now);
    double travelDecay = 0.0;
    KCPoint travel = kc_pt(0.0, 0.0);
    double mouseEase = 0.0;
    KCPoint leftRest = kc_pt(0.0, 0.0);
    KCPoint rightRest = kc_pt(0.0, 0.0);
    double la = 0.0;
    double ra = 0.0;
    KCPoint mousePaw = kc_pt(0.0, 0.0);
    double leftClick = 0.0;
    double rightClick = 0.0;
    double armEase = 0.0;
    double tx = 0.0;
    double ty = 0.0;
    double watchingKeys = 0.0;
    double currentKeyX = 0.0;
    double eyeEase = 0.0;

    s->time = now;
    if (now > s->nextBlink + 0.30) {
        s->nextBlink = now + 2.6 + blinkRandom01 * 3.7;
    }
    s->blink = fmax(kc_pulse(now - s->nextBlink, 0.23), kc_pulse(now - s->lastPet, 0.59));
    typing = 1.0 - kc_smooth((now - s->lastKey - 0.95) / 0.48);
    hold = (s->leftDown != 0 || s->rightDown != 0) ? 1.0 : 1.0 - kc_smooth((now - s->lastMove - 0.55) / 0.28);
    if (office != 0) {
        s->grip = usingMouse != 0 ? 1.0 : 0.0;
    } else {
        s->grip = s->grip + kc_clamp(hold - s->grip, -dt * 10.0, dt * 12.0);
    }
    if (hold == 0.0) {
        travelDecay = exp(-dt * 8.0);
        s->localTravel.x *= travelDecay;
        s->localTravel.y *= travelDecay;
    }
    travel = kc_pt(s->monitorPosition.x * 5.0 + s->localTravel.x,
                   s->monitorPosition.y * 5.0 + s->localTravel.y);
    mouseEase = 1.0 - exp(-dt * 32.0);
    s->mouseOffset.x += (travel.x * s->grip - s->mouseOffset.x) * mouseEase;
    s->mouseOffset.y += (travel.y * s->grip - s->mouseOffset.y) * mouseEase;

    {
        double liftY = office == 0 ? s->catFury * 7.5 : 0.0;
        double restY = (s->catStyle >= 14 && s->catStyle <= 17) ? 217.0 : 189.0;
        double rightRestY = (s->catStyle >= 14 && s->catStyle <= 17) ? 219.0 : 193.0;
        double liftScale = (s->catStyle >= 14 && s->catStyle <= 17) ? 0.40 : 1.0;
        leftRest = kc_mix_point(kc_pt(192.0, 233.0), kc_pt(188.0, restY - liftY * liftScale), typing);
        rightRest = kc_mix_point(kc_pt(229.0, 233.0), kc_pt(233.0, rightRestY - liftY * liftScale), typing);
    }
    if (office != 0) {
        double leftHomeX = fmin(s->jKey.x, 204.0);
        double rightHomeX = fmax(s->fKey.x, 249.0);
        s->officeLeft = (usingMouse != 0 || now < s->officeTwoHandsUntil || (now - s->leftAt < 0.8)) ? 1 : 0;
        s->officeRight = ((now - s->rightAt < 0.8) || (now < s->officeTwoHandsUntil && (now - s->lastKey < 0.8))) ? 1 : 0;
        leftRest = kc_pt(leftHomeX, s->jKey.y - 4.0 * typing);
        rightRest = (s->officeLeft != 0 && s->officeRight != 0 && usingMouse == 0)
            ? kc_pt(rightHomeX, s->fKey.y)
            : s->spaceKey;
        rightRest.y -= 4.0 * typing;
        if (s->previousCharacter != s->catStyle) {
            s->right = rightRest;
        }
    }
    s->previousCharacter = s->catStyle;

    la = kc_beat(now - s->leftAt);
    ra = kc_beat(now - s->rightAt);
    leftRest = kc_mix_point(leftRest, s->leftKey, la);
    rightRest = kc_mix_point(rightRest, s->rightKey, ra);
    if (office != 0 && s->officeLeft != 0 && s->officeRight != 0 && usingMouse == 0) {
        double leftHomeX = fmin(s->jKey.x, 204.0);
        double rightHomeX = fmax(s->fKey.x, 249.0);
        if (s->lastOfficeStrikeWasLeft != 0 && s->rightKey.x < 214.0) {
            rightRest = kc_pt(rightHomeX, s->fKey.y - 4.0 * typing);
        } else if (s->lastOfficeStrikeWasLeft == 0 && s->leftKey.x > 226.0) {
            leftRest = kc_pt(leftHomeX, s->jKey.y - 4.0 * typing);
        }
        KCPoint strike = s->lastOfficeStrikeWasLeft != 0 ? leftRest : rightRest;
        KCPoint other = s->lastOfficeStrikeWasLeft != 0 ? rightRest : leftRest;
        if (fabs(strike.x - other.x) < 16.0 && fabs(strike.y - other.y) < 13.0) {
            other.x = strike.x + (strike.x < s->spaceKey.x ? 17.0 : -17.0);
            other.y -= 3.0;
        }
        if (strike.x < other.x) {
            leftRest = strike;
            rightRest = other;
        } else {
            leftRest = other;
            rightRest = strike;
        }
    }

    leftClick = kc_click_glow(now, s->leftDown, s->leftClickAt);
    rightClick = kc_click_glow(now, s->rightDown, s->rightClickAt);
    s->leftClickGlow = leftClick;
    s->rightClickGlow = rightClick;

    mousePaw = kc_pt(s->mousePaw.x + s->mouseOffset.x + (leftClick - rightClick) * 1.6,
                     s->mousePaw.y + s->mouseOffset.y + 2.6 * fmax(leftClick, rightClick));
    armEase = 1.0 - exp(-dt * 46.0);
    s->freeLeft = kc_mix_point(s->freeLeft, leftRest, armEase);
    s->left = kc_mix_point(s->freeLeft, mousePaw, s->grip);
    s->right = kc_mix_point(s->right, rightRest, armEase);
    s->leftActivity = office == 0 ? kc_clamp(fmax(typing, fmax(s->grip, la * s->catSlam)), 0.0, 1.0) : fmax(typing, s->grip);
    s->rightActivity = office == 0 ? kc_clamp(fmax(typing, ra * s->catSlam), 0.0, 1.0) : typing;

    tx = s->pointerKnown != 0 ? tanh((s->pointer.x - 214.0) / 155.0) : 0.0;
    ty = s->pointerKnown != 0 ? tanh((s->pointer.y - 85.0) / 155.0) : 0.0;
    watchingKeys = typing * (1.0 - s->grip * 0.55);
    currentKeyX = s->leftAt >= s->rightAt ? s->leftKey.x : s->rightKey.x;
    tx += (tanh((currentKeyX - 214.0) / 130.0) - tx) * watchingKeys;
    ty += (1.0 - ty) * watchingKeys;
    eyeEase = 1.0 - exp(-dt * 14.0);
    s->gazeX += (tx - s->gazeX) * eyeEase;
    s->gazeY += (ty - s->gazeY) * eyeEase;

    if (office != 0) {
        s->catSlam = 0.0;
        s->catFury = 0.0;
        s->catFastAt = now - 10.0;
        s->catStrikeSpeed = 0.0;
        s->catLeanX = 0.0;
        s->catBounceY = 0.0;
        s->catTiltDeg = 0.0;
        s->deskOffsetX = 0.0;
        s->deskOffsetY = 0.0;
        s->deskTiltDeg = 0.0;
    } else {
        int recentFast = (now - s->catFastAt < 0.32 && (s->catStrikeSpeed > 0.0 || s->catFastAt > s->lastKey)) ? 1 : 0;
        double furyDecay = recentFast != 0 ? exp(-dt * 0.55) : exp(-dt * 4.2);
        double fastAge = fmax(0.0, now - s->catFastAt);
        double keyPulse = fastAge <= 0.18
            ? exp(-fastAge * 9.5) * (1.0 - kc_smooth((fastAge - 0.11) / 0.07))
            : 0.0;
        KCPoint activeKey = s->leftAt >= s->rightAt ? s->leftKey : s->rightKey;
        double activePower = kc_clamp(fmax(s->catFury, s->catSlam), 0.0, 1.0);
        double hitPulse = activePower * fmax(kc_beat(fastAge), keyPulse);
        double keyRecent = (now - s->catFastAt < 0.90 && activePower > 0.0) ? 1.0 : 0.0;
        double keyDir = keyRecent * tanh((activeKey.x - 214.0) / 68.0);
        double mouseDamp = 1.0 - 0.55 * s->grip;
        double rock = (s->catLastStrikeWasLeft != 0 ? -1.0 : 1.0) * (2.4 + 3.4 * s->catFury) * hitPulse * mouseDamp;
        double buzz = s->catFury > 0.25 ? sin(now * 42.0) * (0.95 * (s->catFury - 0.25) / 0.75 * (0.35 + 0.65 * hitPulse)) : 0.0;
        double targetLean = keyDir * (4.8 * s->catFury + 3.6 * hitPulse) + buzz;
        double targetBounce = hitPulse * (5.2 + 3.4 * s->catFury);
        double targetTilt = (keyDir * (3.5 * s->catFury + 2.3 * hitPulse)) * mouseDamp + rock + buzz * 0.65;
        double motionEase = 1.0 - exp(-dt * 26.0);
        double deskEase = 1.0 - exp(-dt * 30.0);

        s->catFury *= furyDecay;
        s->catSlam = fmax(s->catSlam * exp(-dt * 14.5), keyPulse);
        s->catLeanX += (targetLean - s->catLeanX) * motionEase;
        s->catBounceY += (targetBounce - s->catBounceY) * motionEase;
        s->catTiltDeg += (targetTilt - s->catTiltDeg) * motionEase;
        s->deskOffsetX += (keyDir * s->catSlam * (1.8 + 1.5 * s->catFury) + buzz * 0.45 - s->deskOffsetX) * deskEase;
        s->deskOffsetY += (s->catSlam * (2.8 + 2.5 * s->catFury) - s->deskOffsetY) * deskEase;
        s->deskTiltDeg += (keyDir * s->catSlam * (1.2 + 1.0 * s->catFury) - s->deskTiltDeg) * deskEase;
        if (s->catFury < 0.002 && s->catSlam < 0.002 &&
            fabs(s->catLeanX) < 0.01 && fabs(s->catBounceY) < 0.01 && fabs(s->catTiltDeg) < 0.01) {
            s->catFury = 0.0;
            s->catSlam = 0.0;
            s->catLeanX = 0.0;
            s->catBounceY = 0.0;
            s->catTiltDeg = 0.0;
            s->deskOffsetX = 0.0;
            s->deskOffsetY = 0.0;
            s->deskTiltDeg = 0.0;
        }
    }

    if (office != 0 && kc_scene_office_pose(s) == KC_POSE_WORKING) {
        double keyRecency = 1.0 - kc_smooth((now - s->lastKey - 0.25) / 0.55);
        double lastStrike = fmax(s->leftAt, s->rightAt);
        double keyBeat = kc_beat(now - lastStrike);
        KCPoint activeKey = s->leftAt >= s->rightAt ? s->leftKey : s->rightKey;
        double keyDirX = tanh((activeKey.x - s->spaceKey.x) / 62.0);
        double keyRowFactor = kc_clamp((s->spaceKey.y - activeKey.y) / 36.0, 0.0, 1.0);
        double rock = (s->lastOfficeStrikeWasLeft != 0 ? -0.95 : 0.95) * keyBeat;
        double keyLeanX = keyDirX * (6.2 + 1.8 * keyBeat);
        double keyLeanY = keyBeat * 2.5 + keyRowFactor * 1.2 + (0.5 + 0.5 * sin(now * 2.8)) * 0.65;
        double keyTilt = keyDirX * (3.1 + 0.9 * keyBeat) + rock;

        double mouseDirX = tanh((s->mousePaw.x + s->mouseOffset.x - s->spaceKey.x) / 95.0);
        double mouseBeat = fmax(leftClick, rightClick);
        double mouseLeanX = mouseDirX * 4.5 + s->mouseOffset.x * 0.22;
        double mouseLeanY = mouseBeat * 1.4 + sin(now * 1.8) * 0.35;
        double mouseTilt = mouseDirX * 2.2;

        double mouseWeight = s->grip * (1.0 - 0.65 * keyRecency);
        double targetLeanX = keyLeanX * keyRecency + (mouseLeanX - keyLeanX * keyRecency) * mouseWeight;
        double targetLeanY = keyLeanY * keyRecency + (mouseLeanY - keyLeanY * keyRecency) * mouseWeight;
        double targetTilt = keyTilt * keyRecency + (mouseTilt - keyTilt * keyRecency) * mouseWeight;
        double bodyEase = 1.0 - exp(-dt * 15.0);

        s->officeLeanX += (targetLeanX - s->officeLeanX) * bodyEase;
        s->officeLeanY += (targetLeanY - s->officeLeanY) * bodyEase;
        s->officeTiltDeg += (targetTilt - s->officeTiltDeg) * bodyEase;
    } else {
        s->officeLeanX = 0.0;
        s->officeLeanY = 0.0;
        s->officeTiltDeg = 0.0;
    }
    {
        int inBurst = office == 0 ? (s->catFury >= 0.58 ? 1 : 0) : (now < s->officeTwoHandsUntil ? 1 : 0);
        double lastInput = fmax(s->lastKey, s->lastMove);
        if (inBurst != 0 && s->lastBurstActive == 0) {
            s->todayBurstCount++;
        }
        s->lastBurstActive = inBurst;
        if (s->leftDown != 0 || s->rightDown != 0 || (lastInput >= 0.0 && now - lastInput < 5.0)) {
            s->activeWorkSeconds += dt;
        } else if (lastInput < 0.0 || now - lastInput >= 300.0) {
            s->activeWorkSeconds = 0.0;
        }
        if (s->breakReminderEnabled != 0 && s->activeWorkSeconds >= 2700.0 && now >= s->breakBubbleUntil) {
            s->breakBubbleUntil = now + 14.0;
            s->activeWorkSeconds = 0.0;
        }
    }
}

KCCapLayout kc_cap_layout(double age, int isNewest, KCPoint start, double angleDeg) {
    KCCapLayout r;
    double clampedAge = fmax(0.0, age);
    double progress = clampedAge / 0.89;
    double inv = 1.0 - kc_clamp(progress, 0.0, 1.0);
    double travel = 1.0 - inv * inv;
    double settle = isNewest != 0 ? kc_smooth((clampedAge - 0.60) / 0.42) : 0.0;
    KCPoint drift = kc_pt(start.x + angleDeg * travel, start.y - 53.0 * travel);
    double lifetime = isNewest != 0 ? 5.8 : 0.89;
    r.position = kc_mix_point(drift, kc_pt(298.0, 214.0), settle);
    r.opacity = isNewest != 0 ? 1.0 - kc_smooth((clampedAge - 5.4) / 0.4)
                              : 1.0 - kc_smooth((progress - 0.55) / 0.45);
    r.angleDeg = angleDeg * travel * (1.0 - settle);
    r.expired = clampedAge > lifetime ? 1 : 0;
    return r;
}

KCOfficeArmGeometry kc_solve_office_arm(KCPoint shoulder, KCPoint target, int side, int resting) {
    KCOfficeArmGeometry r;
    double handScale = 1.16;
    double pi = 3.14159265358979323846;
    double angle = kc_clamp(-(target.x - shoulder.x) * 0.34, -31.0, 31.0);
    double rad = angle * pi / 180.0;
    KCPoint down = kc_pt(-sin(rad), cos(rad));
    KCPoint palm = kc_shift(target, down, -5.5 * handScale);
    KCPoint wrist = kc_shift(palm, down, -7.4 * handScale);
    KCPoint reach = kc_sub(wrist, shoulder);
    double reachLen = kc_hypot2(reach.x, reach.y);
    double bend = resting != 0 ? 9.0 : kc_clamp(26.0 - reachLen * 0.15, 6.0, 16.0);
    KCPoint elbow = kc_shift(kc_shift(shoulder, reach, 0.54), kc_normal(kc_unit(reach)), -side * bend);
    KCPoint upper = kc_unit(kc_sub(elbow, shoulder));
    KCPoint lower = kc_unit(kc_sub(wrist, elbow));
    KCPoint a = kc_normal(upper);
    KCPoint b = kc_normal(kc_unit(kc_add(upper, lower)));
    KCPoint c = kc_normal(lower);
    KCPoint end = kc_shift(wrist, lower, -3.6);
    double upperLen = kc_hypot2(elbow.x - shoulder.x, elbow.y - shoulder.y);
    double lowerLen = kc_hypot2(end.x - elbow.x, end.y - elbow.y);
    double radius = fmin(13.0, fmin(upperLen, lowerLen) * 0.38);
    KCPoint before = kc_shift(elbow, upper, -radius);
    KCPoint after = kc_shift(elbow, lower, radius);
    double dotAB = a.x * b.x + a.y * b.y;
    double jointWidth = 8.5 / fmax(0.5, dotAB);
    KCPoint handNormal = kc_normal(down);
    KCPoint cuffEdge = kc_shift(palm, down, -6.5 * handScale);

    r.sleeveStart = kc_shift(shoulder, a, 6.8);
    r.c1a = kc_shift(kc_shift(shoulder, upper, 9.0), a, 7.8);
    r.c1b = kc_shift(kc_shift(before, upper, -4.0), a, 8.2);
    r.before = kc_shift(before, a, 8.2);
    r.elbowJointOuter = kc_shift(elbow, b, jointWidth);
    r.after = kc_shift(after, c, 8.2);
    r.c2a = kc_shift(kc_shift(after, lower, 4.0), c, 8.2);
    r.c2b = kc_shift(kc_shift(end, lower, -6.0), c, 6.2);
    r.endOuter = kc_shift(end, c, 5.7);
    r.endInner = kc_shift(end, c, -5.7);
    r.c3a = kc_shift(kc_shift(end, lower, -6.0), c, -6.2);
    r.c3b = kc_shift(kc_shift(after, lower, 4.0), c, -8.2);
    r.afterInner = kc_shift(after, c, -8.2);
    r.elbowJointInner = kc_shift(elbow, b, -jointWidth);
    r.beforeInner = kc_shift(before, a, -8.2);
    r.c4a = kc_shift(kc_shift(before, upper, -4.0), a, -8.2);
    r.c4b = kc_shift(kc_shift(shoulder, upper, 9.0), a, -7.8);
    r.shoulderInner = kc_shift(shoulder, a, -6.8);
    r.c5a = kc_shift(kc_shift(shoulder, a, -6.8), upper, -3.5);
    r.c5b = kc_shift(kc_shift(shoulder, a, 6.8), upper, -3.5);

    r.creaseStart = kc_shift(elbow, b, side * 5.4);
    r.creaseEnd = kc_shift(kc_shift(elbow, lower, 3.5), b, side * 2.4);

    r.cuff0 = kc_shift(end, c, 5.0);
    r.cuff1 = kc_shift(end, c, -5.0);
    r.cuff2 = kc_shift(cuffEdge, handNormal, -4.0 * handScale);
    r.cuff3 = kc_shift(cuffEdge, handNormal, 4.0 * handScale);

    r.edgeStart = kc_shift(end, c, -5.0);
    r.edgeEnd = kc_shift(end, c, 5.0);

    r.palm = palm;
    r.handAngleDeg = angle;
    r.handScaleX = -side * handScale;
    r.handScaleY = handScale;
    return r;
}

static KCPoint kc_pivot_rotate(KCPoint p, double cx, double cy, double rad, double dx, double dy) {
    double px = p.x - cx;
    double py = p.y - cy;
    double c = cos(rad);
    double s = sin(rad);
    return kc_pt(cx + dx + px * c - py * s, cy + dy + px * s + py * c);
}

KCOfficeBodyPose kc_office_body_pose(const KCSceneState *s, double centerX, double bottomY,
                                     KCPoint leftShoulder, KCPoint rightShoulder) {
    KCOfficeBodyPose r;
    double pi = 3.14159265358979323846;
    double rad = s->officeTiltDeg * pi / 180.0;
    r.offsetX = s->officeLeanX;
    r.offsetY = s->officeLeanY;
    r.tiltDeg = s->officeTiltDeg;
    r.leftShoulder = kc_pivot_rotate(leftShoulder, centerX, bottomY, rad, s->officeLeanX, s->officeLeanY);
    r.rightShoulder = kc_pivot_rotate(rightShoulder, centerX, bottomY, rad, s->officeLeanX, s->officeLeanY);
    return r;
}

KCCatMotion kc_cat_motion(const KCSceneState *s) {
    KCCatMotion r;
    double squash = 0.0;
    double usingMouse = 0.0;
    double la = 0.0;
    double ra = 0.0;
    double whipGate = 0.0;
    if (kc_is_person(s->catStyle) != 0) {
        r.offsetX = 0.0;
        r.offsetY = 0.0;
        r.tiltDeg = 0.0;
        r.scaleX = 1.0;
        r.scaleY = 1.0;
        r.slam = 0.0;
        r.fury = 0.0;
        r.leftWhip = 0.0;
        r.rightWhip = 0.0;
        r.deskOffsetX = 0.0;
        r.deskOffsetY = 0.0;
        r.deskTiltDeg = 0.0;
        return r;
    }
    squash = kc_clamp(s->catSlam * (0.048 + 0.032 * s->catFury), 0.0, 0.09);
    usingMouse = kc_scene_using_mouse(s, s->time) != 0 ? 1.0 : 0.0;
    la = kc_beat(s->time - s->leftAt);
    ra = kc_beat(s->time - s->rightAt);
    whipGate = s->catFury > 0.01 ? kc_clamp(s->catFury / 0.25, 0.0, 1.0) : 0.0;
    r.offsetX = s->catLeanX;
    r.offsetY = s->catBounceY;
    r.tiltDeg = s->catTiltDeg;
    r.scaleX = 1.0 + squash;
    r.scaleY = 1.0 - squash * 1.12;
    r.slam = s->catSlam;
    r.fury = s->catFury;
    r.leftWhip = whipGate * (1.0 - usingMouse) * la * (0.45 + 0.55 * s->catFury);
    r.rightWhip = whipGate * ra * (0.45 + 0.55 * s->catFury);
    r.deskOffsetX = s->deskOffsetX;
    r.deskOffsetY = s->deskOffsetY;
    r.deskTiltDeg = s->deskTiltDeg;
    if (s->fxLevel == KC_FX_QUIET) {
        r.offsetX *= 0.45;
        r.offsetY *= 0.45;
        r.tiltDeg *= 0.45;
        r.scaleX = 1.0 + (r.scaleX - 1.0) * 0.45;
        r.scaleY = 1.0 - (1.0 - r.scaleY) * 0.45;
        r.slam *= 0.35;
        r.fury = fmin(r.fury, 0.27);
        r.leftWhip *= 0.50;
        r.rightWhip *= 0.50;
        r.deskOffsetX = 0.0;
        r.deskOffsetY = 0.0;
        r.deskTiltDeg = 0.0;
    }
    return r;
}

double kc_cat_impact_intensity(const KCSceneState *s) {
    if (s->fxLevel == KC_FX_QUIET || kc_is_person(s->catStyle) != 0 || s->catStrikeSpeed <= 0.0 || s->catFury < 0.20) {
        return 0.0;
    }
    return kc_clamp(fmax(0.32, s->catFury), 0.32, 1.0);
}

double kc_scene_break_opacity(const KCSceneState *s, double now) {
    double rem = 0.0;
    double age = 0.0;
    if (s->breakReminderEnabled == 0 || now >= s->breakBubbleUntil) {
        return 0.0;
    }
    rem = s->breakBubbleUntil - now;
    age = 14.0 - rem;
    if (age < 0.0) {
        return 0.0;
    }
    return fmin(kc_smooth(age / 0.35), kc_smooth(rem / 0.85));
}

void kc_scene_dismiss_break(KCSceneState *s) {
    s->breakBubbleUntil = -10.0;
    s->activeWorkSeconds = 0.0;
}

KCImpactLayout kc_impact_layout(double age, double intensity, double seedAngleDeg) {
    KCImpactLayout r;
    double clamped = fmax(0.0, age);
    double t = kc_clamp(clamped / 0.28, 0.0, 1.0);
    double inv = 1.0 - t;
    double ease = 1.0 - inv * inv * inv;
    double power = kc_clamp(intensity, 0.25, 1.0);
    r.ringRadius = 4.5 + (16.0 + 13.0 * power) * ease;
    r.innerRadius = 2.0 + (10.5 + 9.5 * power) * kc_clamp(t * 1.25, 0.0, 1.0);
    r.ringOpacity = (1.0 - kc_smooth((t - 0.15) / 0.85)) * (0.65 + 0.35 * power);
    r.sparkScale = (0.60 + 0.70 * power) * (1.0 - 0.30 * t);
    r.angleOffsetDeg = seedAngleDeg;
    r.expired = clamped > 0.28 ? 1 : 0;
    return r;
}

int kc_compare_version_triples(int aMaj, int aMin, int aPat, int bMaj, int bMin, int bPat) {
    if (aMaj != bMaj) {
        return aMaj < bMaj ? -1 : 1;
    }
    if (aMin != bMin) {
        return aMin < bMin ? -1 : 1;
    }
    if (aPat != bPat) {
        return aPat < bPat ? -1 : 1;
    }
    return 0;
}
