#include <linux/input.h>
#include <stddef.h>
#include <stdio.h>

int main(void) {
    _Static_assert(sizeof(struct ff_effect) == 48, "ff_effect must match the complete native union");
    _Static_assert(sizeof(struct input_event) == 24, "64-bit input_event layout");
    _Static_assert(offsetof(struct ff_effect, replay.length) == 10, "bounded replay duration");
    _Static_assert(offsetof(struct ff_effect, u) == 16, "effect union offset");
    _Static_assert(offsetof(struct ff_effect, u.condition[1].right_coeff) == 32, "second actuator coefficient");
    _Static_assert(EVIOCSFF == 0x40304580, "upload ioctl");
    _Static_assert(EVIOCRMFF == 0x40044581, "remove ioctl");
    _Static_assert(EVIOCGBIT(EV_FF, 16) == 0x80104535, "capability bitmap ioctl");
    puts("PASS: Linux feedback envelope, ioctl word sizes and actuator coefficients match kernel headers.");
    return 0;
}
