// Disposable macOS CI diagnostic. Reproduce the old overwrite inside a guarded envelope,
// without corrupting a process stack, then check the requested REV0 against Apple's SDK.
#include <mach/mach.h>
#include <stddef.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>

int main(void) {
    _Static_assert(sizeof(mach_port_t) == 4, "Mach ports are 32 bit");
    _Static_assert(sizeof(mach_msg_type_number_t) == 4, "Mach counts are 32 bit");
    _Static_assert(HOST_VM_INFO64_REV0_COUNT == 24, "REV0 is 24 words");
    _Static_assert(offsetof(vm_statistics64_data_t, free_count) == 0, "free offset");
    _Static_assert(offsetof(vm_statistics64_data_t, inactive_count) == 8, "inactive offset");
    _Static_assert(offsetof(vm_statistics64_data_t, decompressions) == 96, "REV0 envelope");

    host_t host = mach_host_self();
    struct { integer_t legacy[13]; integer_t guard[51]; } envelope;
    memset(&envelope, 0xA5, sizeof(envelope));
    mach_msg_type_number_t count = 13 * sizeof(integer_t); // Old caller's incorrect count.
    kern_return_t result = host_statistics64(host, HOST_VM_INFO64, envelope.legacy, &count);
    int overwritten = 0;
    for (size_t index = 0; index < 51; index++)
        if ((uint32_t)envelope.guard[index] != UINT32_C(0xA5A5A5A5)) overwritten++;
    printf("Legacy: allocated=13 words, advertised=52 words, returned=%u, overwritten guard words=%d, status=%d\n",
           count, overwritten, result);

    struct { integer_t words[24]; uint32_t guard[8]; } bounded;
    memset(&bounded, 0xA5, sizeof(bounded));
    count = HOST_VM_INFO64_REV0_COUNT;
    kern_return_t fixed_result = host_statistics64(host, HOST_VM_INFO64, bounded.words, &count);
    int intact = 1;
    for (size_t index = 0; index < 8; index++)
        if (bounded.guard[index] != UINT32_C(0xA5A5A5A5)) intact = 0;
    printf("REV0: allocated=24 words, returned=%u, guard intact=%d, status=%d\n", count, intact, fixed_result);
    mach_port_deallocate(mach_task_self(), host);
    return result == KERN_SUCCESS && overwritten > 0 && fixed_result == KERN_SUCCESS
        && count == HOST_VM_INFO64_REV0_COUNT && intact ? 0 : 1;
}
