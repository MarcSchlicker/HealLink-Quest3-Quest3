# HealLink Unity 6000.6 compatibility patch

Based on Meta Interaction SDK 201.0.0. Embedded package preserves upstream licensing.

Pose detectors, editor tools and sample effects now use EntityId for Unity object identity. ID dictionaries retain the full EntityId; the migration window serializes IDs through GetRawData/FromULong. Editor/sample changes target Unity 6000.6+, not older Unity versions.

Validation: Oculus.Interaction.Samples and Oculus.Interaction.Editor compiled successfully using the Unity 6000.6.7f1 compiler and project response files. Review and remove patches when upgrading to a compatible upstream release.
