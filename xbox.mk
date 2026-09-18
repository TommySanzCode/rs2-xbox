# eval "$(../nxdk/bin/activate -s)"
NXDK_DIR ?= $(CURDIR)/.deps/nxdk
XBOX_RAM_MB ?= 64
ifneq ($(filter $(XBOX_RAM_MB),64 128),$(XBOX_RAM_MB))
$(error XBOX_RAM_MB must be 64 or 128)
endif

DEBUG = 0

ifeq ($(DEBUG),1)
DEBUG = y
else
CFLAGS += -O3
endif

XBE_TITLE = client
GEN_XISO = $(XBE_TITLE).iso
SRCS := $(shell find src -type f -name '*.c')
OUTPUT_DIR = rom
LTO = y
CFLAGS += -Wall -Dclient
CFLAGS += -DXBOX_RAM_MB=$(XBOX_RAM_MB)
CFLAGS += -DWITH_RSA_LIBTOM -DMP_NO_DEV_URANDOM -U_WIN32
NXDK_SDL = y

include $(NXDK_DIR)/Makefile

# Changing profiles must rebuild client objects, even in a reused checkout.
PROFILE_STAMP := build/xbox-profile-$(XBOX_RAM_MB)
$(shell mkdir -p build)
ifneq ($(shell cat build/xbox-active-profile 2>/dev/null),$(XBOX_RAM_MB))
$(shell echo $(XBOX_RAM_MB) > build/xbox-active-profile; touch $(PROFILE_STAMP))
endif
$(OBJS): $(PROFILE_STAMP)
$(PROFILE_STAMP):
	@touch $@

# cxbe defaults to the 64 MB limit. Adjust only our unsigned homebrew header
# before making the disc image, so XBE and ISO always agree.
.PHONY: xbox-memory-profile
xbox-memory-profile: $(OUTPUT_DIR)/default.xbe
	python3 scripts/set-xbe-memory.py $(OUTPUT_DIR)/default.xbe $(XBOX_RAM_MB)
all: xbox-memory-profile
$(GEN_XISO): xbox-memory-profile

# Refresh the image on every build, including config/cache changes or removals.
.PHONY: xbox-assets
$(GEN_XISO): xbox-assets
