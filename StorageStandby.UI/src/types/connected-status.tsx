
export const CONNECTED_STATUS = {
    Unconnected: 0,
    Connected: 1,
    Reauthenticate: 2
}

export type ConnectedStatus = typeof CONNECTED_STATUS[keyof typeof CONNECTED_STATUS]

export type ConnectedStatusMetadata = {
    name: string
}

export const CONNECTED_STATUS_METADATA: Record<ConnectedStatus, ConnectedStatusMetadata> = {
    [CONNECTED_STATUS.Unconnected]: {
        name: "Unconnected"
    },
    [CONNECTED_STATUS.Connected]: {
        name: "Connected"
    },
    [CONNECTED_STATUS.Reauthenticate]: {
        name: "Reauthenticate"
    },
}

export const PROVIDER_ORDER: ConnectedStatus[] = [CONNECTED_STATUS.Unconnected, CONNECTED_STATUS.Connected, CONNECTED_STATUS.Reauthenticate]

export const getConnectedStatusName = (status: ConnectedStatus) => CONNECTED_STATUS_METADATA[status].name
