import { LocationDescriptor } from "history";
import { $c } from "property-chain";
import * as React from "react";
import { withUserInfoStrict } from "Commons/AuthProviders/AuthProviders";
import { DelayedLoader } from "Commons/DelayedLoader/DelayedLoader";
import { ErrorHandlingContainer } from "Commons/ErrorHandling";
import { takeLastAndRejectPrevious } from "Commons/Utils/PromiseUtils";
import { ApiError } from "Domain/ApiBase/ApiBase";
import { RemoteTaskInfoModel } from "Domain/EDI/Api/RemoteTaskQueue/RemoteTaskInfoModel";
import { IRemoteTaskQueueApi, withRemoteTaskQueueApi } from "Domain/EDI/Api/RemoteTaskQueue/RemoteTaskQueue";
import { ReactApplicationUserInfo } from "Domain/EDI/ReactApplicationUserInfo";
import { SuperUserAccessLevels } from "Domain/Globals";

import { TaskDetailsPage } from "../components/TaskDetailsPage/TaskDetailsPage";
import { TaskNotFoundPage } from "../components/TaskNotFoundPage/TaskNotFoundPage";

interface TaskDetailsPageContainerProps {
    id: string;
    remoteTaskQueueApi: IRemoteTaskQueueApi;
    parentLocation: Nullable<LocationDescriptor>;
    userInfo: ReactApplicationUserInfo;
}

interface TaskDetailsPageContainerState {
    taskDetails: Nullable<RemoteTaskInfoModel>;
    loading: boolean;
    notFoundError: boolean;
}

class TaskDetailsPageContainerInternal extends React.Component<
    TaskDetailsPageContainerProps,
    TaskDetailsPageContainerState
> {
    public state: TaskDetailsPageContainerState = {
        loading: false,
        taskDetails: null,
        notFoundError: false,
    };
    public getTaskDetails = takeLastAndRejectPrevious(
        this.props.remoteTaskQueueApi.getTaskDetails.bind(this.props.remoteTaskQueueApi)
    );

    public componentWillMount() {
        this.loadData(this.props.id);
    }

    public componentWillReceiveProps(nextProps: TaskDetailsPageContainerProps) {
        if (this.props.id !== nextProps.id) {
            this.loadData(nextProps.id);
        }
    }

    public getTaskLocation(id: string): LocationDescriptor {
        const { parentLocation } = this.props;

        return {
            pathname: `/AdminTools/Tasks/${id}`,
            state: { parentLocation: parentLocation },
        };
    }

    public async loadData(id: string): Promise<void> {
        this.setState({ loading: true, notFoundError: false });
        try {
            try {
                const taskDetails = await this.getTaskDetails(id);
                this.setState({ taskDetails: taskDetails });
            } catch (e) {
                if (e instanceof ApiError) {
                    if (e.statusCode === 404) {
                        this.setState({ notFoundError: true });
                        return;
                    }
                }
                throw e;
            }
        } finally {
            this.setState({ loading: false });
        }
    }

    public async handlerRerun(): Promise<void> {
        const { remoteTaskQueueApi, id } = this.props;
        this.setState({ loading: true });
        try {
            await remoteTaskQueueApi.rerunTasks([id]);
            const taskDetails = await this.getTaskDetails(id);
            this.setState({ taskDetails: taskDetails });
        } finally {
            this.setState({ loading: false });
        }
    }

    public async handlerCancel(): Promise<void> {
        const { remoteTaskQueueApi, id } = this.props;
        this.setState({ loading: true });
        try {
            await remoteTaskQueueApi.cancelTasks([id]);
            const taskDetails = await this.getTaskDetails(id);
            this.setState({ taskDetails: taskDetails });
        } finally {
            this.setState({ loading: false });
        }
    }

    public getDefaultParetnLocation(): LocationDescriptor {
        return {
            pathname: "/AdminTools/Tasks",
        };
    }

    public render(): JSX.Element {
        const { taskDetails, loading, notFoundError } = this.state;
        const { parentLocation } = this.props;
        const currentUser = this.props.userInfo;

        return (
            <DelayedLoader active={loading} type="big" simulateHeightToEnablePageScroll data-tid="Loader">
                {notFoundError && (
                    <TaskNotFoundPage parentLocation={parentLocation || this.getDefaultParetnLocation()} />
                )}
                {taskDetails && (
                    <TaskDetailsPage
                        getTaskLocation={id => this.getTaskLocation(id)}
                        parentLocation={parentLocation || this.getDefaultParetnLocation()}
                        allowRerunOrCancel={$c(currentUser)
                            .with(x => x.superUserAccessLevel)
                            .with(x => [SuperUserAccessLevels.God, SuperUserAccessLevels.Developer].includes(x))
                            .return(false)}
                        taskDetails={taskDetails}
                        onRerun={() => {
                            this.handlerRerun();
                        }}
                        onCancel={() => {
                            this.handlerCancel();
                        }}
                    />
                )}
                <ErrorHandlingContainer />
            </DelayedLoader>
        );
    }
}

export const TaskDetailsPageContainer = withUserInfoStrict(withRemoteTaskQueueApi(TaskDetailsPageContainerInternal));
