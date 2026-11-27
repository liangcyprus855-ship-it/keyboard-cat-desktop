#ifndef KEYCAT_CORE_H
#define KEYCAT_CORE_H

#ifdef __cplusplus
extern "C" {
#endif

#define KC_APP_VERSION "1.13.3"
#define KC_APP_BUILD "1133"
#define KC_GITHUB_REPO "liangcyprus855-ship-it/keyboard-cat-desktop"

enum {
    KC_DEFAULT_CAT = 0,
    KC_CAT_STYLE_COUNT = 18,
    KC_DEFAULT_KEYBOARD = 4,
    KC_DEFAULT_MOUSE = 4,
    KC_MIN_SCALE_PERCENT = 1,
    KC_MAX_SCALE_PERCENT = 200,
    KC_SCENE_WIDTH = 480,
    KC_SCENE_HEIGHT = 320,
    KC_WINDOW_WIDTH = 306,
    KC_WINDOW_HEIGHT = 298,
    KC_SCENE_OFFSET_X = 75,
    KC_SCENE_OFFSET_Y = 9,
    KC_DESK_FORWARD = 14,
    KC_FX_QUIET = 0,
    KC_FX_VIBRANT = 1,
    KC_PROP_NONE = 0,
    KC_PROP_COFFEE = 1,
    KC_PROP_ICED = 2,
    KC_PROP_BRICK = 3,
    KC_PROP_MOON = 4
};

typedef enum {
    KC_POSE_IDLE = 0,
    KC_POSE_WORKING = 1,
    KC_POSE_HOVER = 2,
    KC_POSE_CLICK = 3,
    KC_POSE_SLEEPING = 4
} KCOfficePose;

typedef struct {
    double x;
    double y;
} KCPoint;

typedef struct {
    KCPoint position;
    double tiltDeg;
} KCHeadPose;

typedef struct {
    int catStyle;
    int keyboardStyle;
    int mouseStyle;
    int labelsEnabled;
    int privacyMode;
    int fxLevel;
    int deskProp;
    int breakReminderEnabled;
    int todayKeyCount;
    int todayBurstCount;
    int lastBurstActive;
    int pointerKnown;
    int leftDown;
    int rightDown;
    int headHovered;
    int previousCharacter;
    int officeLeft;
    int officeRight;
    int lastOfficeStrikeWasLeft;
    int officeNextKey;
    int catLastStrikeWasLeft;
    int catNextKey;
    int catNextClick;
    double time;
    double lastKey;
    double leftAt;
    double rightAt;
    double lastMove;
    double leftClickAt;
    double rightClickAt;
    double lastPet;
    double nextBlink;
    double gazeX;
    double gazeY;
    double blink;
    double leftActivity;
    double rightActivity;
    double grip;
    double officeClickUntil;
    double lastOfficeInput;
    double officeIdleSince;
    double officeTwoHandsUntil;
    double officeLeanX;
    double officeLeanY;
    double officeTiltDeg;
    double catSlam;
    double catFury;
    double catFastAt;
    double catStrikeSpeed;
    double catLeanX;
    double catBounceY;
    double catTiltDeg;
    double deskOffsetX;
    double deskOffsetY;
    double deskTiltDeg;
    double leftClickGlow;
    double rightClickGlow;
    double activeWorkSeconds;
    double breakBubbleUntil;
    KCPoint left;
    KCPoint freeLeft;
    KCPoint right;
    KCPoint leftKey;
    KCPoint rightKey;
    KCPoint pointer;
    KCPoint lastPointer;
    KCPoint mouseOffset;
    KCPoint localTravel;
    KCPoint monitorPosition;
    KCPoint mousePaw;
    KCPoint spaceKey;
    KCPoint jKey;
    KCPoint fKey;
    double officeRecentKeys[4];
    double catRecentKeys[4];
    double catRecentClicks[4];
} KCSceneState;

typedef struct {
    KCPoint sleeveStart;
    KCPoint c1a;
    KCPoint c1b;
    KCPoint before;
    KCPoint elbowJointOuter;
    KCPoint after;
    KCPoint c2a;
    KCPoint c2b;
    KCPoint endOuter;
    KCPoint endInner;
    KCPoint c3a;
    KCPoint c3b;
    KCPoint afterInner;
    KCPoint elbowJointInner;
    KCPoint beforeInner;
    KCPoint c4a;
    KCPoint c4b;
    KCPoint shoulderInner;
    KCPoint c5a;
    KCPoint c5b;
    KCPoint creaseStart;
    KCPoint creaseEnd;
    KCPoint cuff0;
    KCPoint cuff1;
    KCPoint cuff2;
    KCPoint cuff3;
    KCPoint edgeStart;
    KCPoint edgeEnd;
    KCPoint palm;
    double handAngleDeg;
    double handScaleX;
    double handScaleY;
} KCOfficeArmGeometry;

typedef struct {
    double offsetX;
    double offsetY;
    double tiltDeg;
    KCPoint leftShoulder;
    KCPoint rightShoulder;
} KCOfficeBodyPose;

typedef struct {
    KCPoint position;
    double opacity;
    double angleDeg;
    int expired;
} KCCapLayout;

typedef struct {
    double offsetX;
    double offsetY;
    double tiltDeg;
    double scaleX;
    double scaleY;
    double slam;
    double fury;
    double leftWhip;
    double rightWhip;
    double deskOffsetX;
    double deskOffsetY;
    double deskTiltDeg;
} KCCatMotion;

typedef struct {
    double ringRadius;
    double innerRadius;
    double ringOpacity;
    double sparkScale;
    double angleOffsetDeg;
    int expired;
} KCImpactLayout;

KCPoint kc_pt(double x, double y);
int kc_is_person(int catStyle);
double kc_clamp(double value, double low, double high);
double kc_smooth(double x);
double kc_beat(double age);
double kc_pulse(double age, double duration);
KCPoint kc_mix_point(KCPoint a, KCPoint b, double t);
double kc_cat_lean(const KCSceneState *s);
KCHeadPose kc_cat_head_pose(const KCSceneState *s);

void kc_scene_init(KCSceneState *s, double now);
void kc_scene_reset_office(KCSceneState *s);
void kc_scene_reset_input(KCSceneState *s, double now);
void kc_scene_set_character(KCSceneState *s, int nextCatStyle);
void kc_scene_click_office_head(KCSceneState *s, double now);
int kc_scene_office_pose(const KCSceneState *s);
int kc_scene_using_mouse(const KCSceneState *s, double now);
void kc_scene_mark_mouse(KCSceneState *s, double now);
void kc_scene_mouse_button(KCSceneState *s, int button1or2, int down, double now);
void kc_scene_mouse_scroll(KCSceneState *s, double now);
void kc_scene_pointer(KCSceneState *s, KCPoint p, int leftDown, int rightDown, double now,
                      KCPoint monitorPosition, int headHit);
int kc_scene_strike(KCSceneState *s, int vk, KCPoint target, double now);
void kc_scene_advance(KCSceneState *s, double now, double blinkRandom01);
KCCapLayout kc_cap_layout(double age, int isNewest, KCPoint start, double angleDeg);
KCCatMotion kc_cat_motion(const KCSceneState *s);
double kc_cat_impact_intensity(const KCSceneState *s);
double kc_scene_break_opacity(const KCSceneState *s, double now);
void kc_scene_dismiss_break(KCSceneState *s);
KCImpactLayout kc_impact_layout(double age, double intensity, double seedAngleDeg);
KCOfficeArmGeometry kc_solve_office_arm(KCPoint shoulder, KCPoint target, int side, int resting);
KCOfficeBodyPose kc_office_body_pose(const KCSceneState *s, double centerX, double bottomY,
                                     KCPoint leftShoulder, KCPoint rightShoulder);
int kc_compare_version_triples(int aMaj, int aMin, int aPat, int bMaj, int bMin, int bPat);

#ifdef __cplusplus
}
#endif

#endif
