# Data Model: マルチインスタンス安全切替 (SPEC-911befe7)

## InstanceEntry
- id: string ("host:port")
- host: string
- port: number
- status: `"up" | "down" | "unknown"`
- lastCheckedAt?: ISO datetime

## InstanceRegistry
- entries: InstanceEntry[]
- activeId?: string
- add(entry)
- markStatus(id, status)
- setActive(id)

## ConnectionState
- activeId: string
- previousId?: string
- lastSwitchAt: ISO datetime
- lastError?: string
