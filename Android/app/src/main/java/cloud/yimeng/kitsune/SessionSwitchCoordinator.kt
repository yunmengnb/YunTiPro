package cloud.yimeng.kitsune

/** Keeps only the newest requested node authoritative during serialized session teardown/startup. */
class SessionSwitchCoordinator {
    data class Request(val generation: Long, val nodeId: String?)

    private var generation = 0L
    private var desiredNodeId: String? = null

    @Synchronized fun request(nodeId: String): Request {
        require(nodeId.isNotBlank())
        generation++
        desiredNodeId = nodeId
        return Request(generation, nodeId)
    }

    @Synchronized fun requestStop(): Request {
        generation++
        desiredNodeId = null
        return Request(generation, null)
    }

    @Synchronized fun latest(): Request = Request(generation, desiredNodeId)

    @Synchronized fun isLatest(request: Request): Boolean =
        request.generation == generation && request.nodeId == desiredNodeId
}
