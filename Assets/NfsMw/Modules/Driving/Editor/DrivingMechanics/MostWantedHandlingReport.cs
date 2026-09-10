using System;
using System.Collections.Generic;

namespace NfsMwRemaster.Driving.Editor.DrivingMechanics
{
    /// <summary>Portable evidence only. Nothing in this model is a Unity tuning assignment.</summary>
    [Serializable]
    public sealed class MostWantedHandlingReport
    {
        public int schema = 1;
        public string readerVersion = MostWantedHandlingReader.Version;
        public string gameVersion = "PC Most Wanted VPAK; executable patch/version not identified";
        public string physicalInterpretation = "Unverified; source units retained; no physical conversions or runtime mappings";
        public string upgradeInterpretation = "Ordered references and current/upgrades fields retained; no active stage selected or blended";
        public bool complete = true;
        public List<MostWantedHandlingSource> sources = new List<MostWantedHandlingSource>();
        public List<MostWantedHandlingVehicle> vehicles = new List<MostWantedHandlingVehicle>();
        public List<MostWantedHandlingRecord> records = new List<MostWantedHandlingRecord>();
        public List<MostWantedHandlingIssue> issues = new List<MostWantedHandlingIssue>();
    }

    [Serializable]
    public sealed class MostWantedHandlingSource
    {
        public string relativePath, sha256, afterSha256, verification;
        public long byteLength;
        public bool unchanged;
    }

    [Serializable]
    public sealed class MostWantedHandlingLocation
    {
        public string sourceFile, sha256, segment;
        public int vaultIndex, segmentStart, segmentOffset, packOffset;
    }

    [Serializable]
    public sealed class MostWantedHandlingIssue
    {
        public string code, subject, message;
    }

    [Serializable]
    public sealed class MostWantedHandlingVehicle
    {
        public string recordId, rowKey, name, nameEvidence, model;
        public List<MostWantedHandlingLink> links = new List<MostWantedHandlingLink>();
    }

    [Serializable]
    public sealed class MostWantedHandlingRecord
    {
        public string id, classKey, className, rowKey, parentKey;
        public MostWantedHandlingLocation source;
        public bool inheritanceResolved;
        public List<MostWantedHandlingAncestor> inheritance = new List<MostWantedHandlingAncestor>();
        public List<MostWantedHandlingField> fields = new List<MostWantedHandlingField>();
        public List<MostWantedHandlingLink> links = new List<MostWantedHandlingLink>();
    }

    [Serializable]
    public sealed class MostWantedHandlingAncestor
    {
        public string classKey, rowKey, parentKey;
        public MostWantedHandlingLocation source;
    }

    [Serializable]
    public sealed class MostWantedHandlingField
    {
        public string fieldKey, name, nameEvidence, typeKey, typeName, ownerRowKey;
        public string status, unsupportedReason, arrayHeaderRawHex;
        public bool inherited, isArray;
        public int elementSize, maximum, flags, alignment, layoutOffset, arrayCapacity, arrayCount;
        public MostWantedHandlingLocation definitionSource, source, ownerSource;
        public List<MostWantedHandlingAncestor> inheritance = new List<MostWantedHandlingAncestor>();
        public List<MostWantedHandlingValue> values = new List<MostWantedHandlingValue>();
    }

    /// <summary>
    /// Discriminated by kind. Float bits/text preserve exact data; numericValue is only populated for finite floats.
    /// Integers never travel through float32. Raw bytes are from the original file; relocated bytes include pointer fixups.
    /// </summary>
    [Serializable]
    public sealed class MostWantedHandlingValue
    {
        public int index;
        public string kind, status, unsupportedReason, rawHex, relocatedHex;
        public string floatBits, numberText, text, referenceClassKey, referenceRowKey, definitionKey;
        public double numericValue;
        public int signedValue;
        public uint unsignedValue;
        public bool booleanValue;
        public MostWantedHandlingLocation source;
        public List<MostWantedHandlingComponent> components = new List<MostWantedHandlingComponent>();
        public string[] uninterpretedWords = Array.Empty<string>();
    }

    [Serializable]
    public sealed class MostWantedHandlingComponent
    {
        public string name, floatBits, numberText;
        public double numericValue;
        public bool finite;
    }

    [Serializable]
    public sealed class MostWantedHandlingLink
    {
        public string fieldKey, fieldName, typeKey, targetClassKey, targetRowKey, targetRecordId;
        public int index;
        public string status, reason;
        public MostWantedHandlingLocation source;
    }

    /// <summary>A caller-provided source identity and buffer. Decoding snapshots the buffer before parsing.</summary>
    public sealed class MostWantedHandlingSourceInput
    {
        public string RelativePath { get; }
        public byte[] Bytes { get; }

        public MostWantedHandlingSourceInput(string relativePath, byte[] bytes)
        {
            RelativePath = relativePath ?? throw new ArgumentNullException(nameof(relativePath));
            Bytes = bytes ?? throw new ArgumentNullException(nameof(bytes));
        }
    }
}
